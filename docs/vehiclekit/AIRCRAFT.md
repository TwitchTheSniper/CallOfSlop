# Aircraft in the vehicle kit

The user (2026-11-10): the vehicle kit should generate small aircraft at run time: planes, drones, helicopters and more. Their users are AI factions
(spawned like ground vehicles, crewed by bots) and loot and capture (players find, steal and fly them). The player's own generator stays creative only.
The goal is the tools and systems that make aircraft at run time, not a few hand-made ones. Flight design: the project doc `claude/flight.md`, code guide:
docs/sandbox/CONSTRUCTS.md ("Air and lift", "Flight control", "Jets, rockets and repulsors", "Wings", "Propellers and rotors").

## Survey (phase 1, 2026-11-10)

Flight in the construct game is much further along than "partly built". What a generator needs, and what exists:

| Need | State | Where |
| --- | --- | --- |
| Flight Stabilizer: seat or receiver input to every effector, no wiring | **Works.** Finds thrusters, rotors, gimbals, surfaces and its own gyro, mixes by least squares; keys Auto / Rotorcraft / Plane / Rocket; autopilot (altitude, heading, hover, waypoint, home) | `ConstructFlight.cs`, `ConstructEffectors.cs`; four sizes in `Constructs/Parts/Flight` |
| Per-surface lift with real angle of attack | **Works** near a player; one summed surface further off | `ConstructAero.cs`; `Constructs/Parts/Wings` (wing segments, swept, fabric, tail plane, fin, aileron, elevator, rudder, flap, four sizes) |
| Control surfaces | **Work**, but only by wire: a stabilizer's `pitch`, `yaw`, `left aileron`, `right aileron`, `flaps` outputs (the Light Plane blueprint shows the wiring) | Wings |
| Props, rotors, ducted fans | **Work.** Shaft-driven (an engine's Drive out) or electric (`motorKw`, off the circuit); hands; blade damage | `ConstructRotor.cs`; `Constructs/Parts/Rotors` |
| Helicopter rotors (collective, cyclic), tail rotors | **Work** on a stabilizer's `lift`, `pitch`, `roll`, `yaw` wires | `ConstructRotor.cs` |
| Electric drone rotors and pushers (thrusters) | **Work**; Drone Frame, Battery, Receiver | `Constructs/Parts/Remote`, `tools/constructs/drone_parts.py` |
| Jets, turbojets with afterburners, rockets, repulsors | **Work** | `ConstructBoost.cs`, `ConstructThrust.cs`; `Constructs/Parts/Boost` |
| Balance markers, centre of lift and thrust sums | **Work** (`Construct.LiftCentre`, `ThrustCentre`) | `ConstructFlightPanel.cs` |
| Lift cells, burners (balloons, airships) | **Work** | `ConstructLift.cs` |
| Remote flying (no seat) | **Works**: a Drone Receiver is a seat; a Remote Console flies it | `ConstructRemote.cs` |
| Hand-made examples | Quadcopter, Scout Drone, Sky Crane, Light Plane, Strike Plane, Deck Plane, Fabric Flyer, Small Helicopter, Jet Hover Platform, Crashed Plane and Jet | `Constructs/Blueprints` |

What the kit itself lacked, and phase 1 added:

- **An aircraft path through the kit.** `VehicleRegistry.Make` sent every generator through road-vehicle machinery: kit 2's marks, the shape grammar, roles and the
  spawn drive check (a throttle pulse that must roll it 1 m). An `IAircraftShape` generator now goes through `AircraftKit.Make` (`VehicleFlightCheck.cs`):
  built, then gated by the flight check instead.
- **A flight check** (`VehicleFlightCheck`), the aircraft's `VehicleDriveCheck`: the blueprint's own text built in a world of its own on a flat floor, settled
  (at most 8 degrees off level), then flown from its seat through the stabilizer for 5.5 s: Sprint 2 s (at least 1.5 m up), let go 2 s (the hover hold: at most
  1 m up or down over the last second, at most 8 degrees off level), W 1.5 s (at least 1 m forward), never past 35 degrees. Its line also gives lift over weight.
  `VehicleCheckOptions.Flies` makes the full check (`--gen-vehicle`, `--sweep-vehicle`) run it in place of the road drive and turn.
- **A drone generator** (`Generators/Drone.cs`, below).

Not missing, so not built: no new parts were needed for a quadcopter. Gaps found for later phases are under "Next steps".

## The Drone generator

`Generators/Drone.cs`, shape "Drone", registered in `VehicleRegistry.Extras` so no style ships files of it (`--gen-vehicles` is unchanged) while any style can
make it by name: `--gen-vehicle "Bandit Drone" --seed 7 --out /tmp/d`, `--sweep-vehicle "Bandit Drone" --seeds 20`. Every seed is made at run time and gated by
the flight check, as a spawned ground vehicle is by the drive check. Two families by the seed:

- **Scout** (a quarter): a Drone Frame, four Drone Rotors under its arms, maybe two Drone Pushers, a Drone Battery, a Drone Receiver, a Flight Stabilizer, maybe a camera. About 30 kg.
- **Multirotor**: a 6-Way Hub with square-beam arms (1 to 2 m). Layouts: plus (4 rotors at the arm ends), H (front and back arms ending in T junctions, 4 rotors),
  hex (side arms plus front and back T's, 6), octo (four T's, 8). Lifters: Ducted Fans, or Electric Props (only six or eight of them: four barely lift the pack).
  A Battery Pack under the hub (a second on heavy ones), legs longer than the packs hang, the stabilizer on the hub, a Drone Receiver behind it or, on a big
  ducted craft, a seat; maybe a camera under the front arm. Rotor hands alternate round the hub, so their torques cancel and the stabilizer yaws on them.
- The stabilizer is set to Rotorcraft keys with a max lean of 15 to 30 degrees from the seed. Colours come from the style's `frame` and `body` palettes.
- No weapons (phase 1).

## Next steps

1. **Fixed-wing prop plane generator** (the next phase): fuselage of beams or a lofted hull, wings from the Wings parts (straight, swept, fabric by faction),
   tail plane and fin, ailerons, elevator, rudder wired from the stabilizer's outputs (the Light Plane's wiring is the template), a Turbine or Engine with a
   prop on its Drive out, or an Electric Prop on a Battery Pack; tricycle or tail-dragger gear. Place the centre of mass ahead of `Construct.LiftCentre`
   (balance by moving the tank or battery, then check with the sum). Its flight check: a take-off run (throttle up, S to rotate), a climb, level flight at
   cruise holding height and heading with the autopilot. Then a jet variant (Turbojet, swept wings).
2. **Helicopters**: a Helicopter Rotor on an engine's shaft with a Tail Rotor, wired `lift`, `pitch`, `roll` and `yaw` (the Small Helicopter blueprint);
   coaxial and tandem as odd craft.
3. **Odd craft**: repulsor hover platforms, small airships on lift cells and burners, tilt-rotors (rotors on joints: the stabilizer doesn't map a rotor on a joint yet).
4. **Faction styles**: aircraft slots in `FactionStyle` (which lifters, frames, skins, how big), and each style naming "Drone" in its extras once its look is
   decided (then seeds ship and `--gen-vehicles` covers them). The other session owns `Styles.cs`.
5. **Spawning**: the spawner and convoys (`VehicleSpawn.cs`, `VehicleConvoys.cs`) place ground vehicles; an aircraft needs a spawn in the air or on a pad,
   and AI pilots: bots fly through the stabilizer's autopilot (waypoint, hover, home) by its inputs, which is the natural hook.
6. **Loot and capture**: a parked or crashed generated aircraft with a Chest (`Loot.json`'s `aircraft` table), a Remote Console found with a drone, the
   receiver's channel as the key to steal one.
7. **Roles and weapons**: gun drones and bombers once the vehicle-weapons work (VehicleArmoury, ConstructWeapons) lands.
8. **Check gaps**: the flight check doesn't look at blade discs overlapping each other or the frame (the generator spaces them by its own sums), at
   endurance (battery minutes), or at a round world (it flies on a flat map, as the drive check drives on one).
