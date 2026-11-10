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
  1 m up or down over the last second, at most 8 degrees off level), W 1.5 s (at least 1 m forward), never past 35 degrees. Its line also gives lift over weight; a craft that won't climb is refused with what its lifters push against its weight and its charge.
  `VehicleCheckOptions.Flies` makes the full check (`--gen-vehicle`, `--sweep-vehicle`) run it in place of the road drive and turn.
- **A drone generator** (`Generators/Drone.cs`, below).

Not missing, so not built: no new parts were needed for a quadcopter. `VehicleVariety` now takes a vehicle with no wheels (a wheelbase and track of 0).

Measured (`--sweep-vehicle "Bandit Drone" --seeds 30`: 30 of 30 pass, 18 parts on average, about 1 s a generation with its flight check; `"IT Drone"` 12 of 12):
climbs of 8 to 11 m in the 2 s, hover drift under 0.5 m, lift 1.4 to 2.2 times the weight. Gaps found for later phases are under "Next steps".

## The Drone generator

`Generators/Drone.cs`, shape "Drone", registered in `VehicleRegistry.Extras` so no style ships files of it (`--gen-vehicles` is unchanged) while any style can
make it by name: `--gen-vehicle "Bandit Drone" --seed 7 --out /tmp/d`, `--sweep-vehicle "Bandit Drone" --seeds 20`. Every seed is made at run time and gated by
the flight check, as a spawned ground vehicle is by the drive check. Two families by the seed:

- **Scout** (a fifth): a Drone Frame, four Drone Rotors under its arms, maybe two Drone Pushers, a Drone Battery, a Drone Receiver, a Flight Stabilizer, maybe a camera. About 33 kg.
- **Tube octo** (a quarter): a Battery for a hub, four 1 m steel tubes, two Drone Rotors hung under each, the stabilizer and receiver on the back tube. About 60 kg.
- **Multirotor** (the rest): a 6-Way Hub with square-beam arms (1 to 2 m) and Ducted Fans. Layouts: plus (4 fans at the arm ends), H (front and back arms
  ending in T junctions, 4), hex (side arms plus front and back T's, 6), octo (four T's, 8). A Battery Pack under the hub (two on half the hexes and octos),
  legs longer than the packs hang, the stabilizer on the hub, a Drone Receiver behind it or, on a 2 m frame, sometimes a seat; maybe a camera under the
  front arm. Fan hands alternate round the hub, so their torques cancel and the stabilizer yaws on them. 350 to 650 kg.
- Lifters were chosen by this game's gravity (18 m/s2, `World.Gravity`): an Electric Prop gives about 40 kg of lift, so it can't carry a Battery Pack (150 kg)
  and is left for planes; a Ducted Fan carries about 140 kg; a Drone Rotor about 11 kg.
- The seed's bits are mixed before the dice (`Drone.Mixed`): `Dice`'s first roll of a fork follows a seed's low bits, and seeds 4 and 5 rolled the same craft.
- The stabilizer is set to Rotorcraft keys with a max lean of 15 to 30 degrees from the seed. Colours come from the style's `frame` and `body` palettes.
- No weapons (phase 1).

## The Plane generator (phase 2, unfinished)

`Generators/Plane.cs`, shape "Plane", in `VehicleRegistry.Extras` (no seeds ship): `--gen-vehicle "Bandit Plane" --seed 3`, `--sweep-vehicle "Bandit Plane" --seeds 12`.
The Light Plane's wiring on a cockpit Frame Block and a beam spine: a seat into a Flight Stabilizer on Plane keys, its `throttle` into each prop, `pitch` into the
elevator, `yaw` into the rudder, `left aileron` and `right aileron` into the outer panels' ailerons, `flaps` into flaps on the inner panels (half the seeds).
From the seed: a single tractor Turbine with a Three-Blade Prop, twin Turbines on the inner panels' fronts (opposite hands), or a pusher Turbine on a 1 m pylon
(prop set `pusher`); low (tank under the spine), mid (wings on the spine's sides) or high (tank on top) wing of three or four straight, narrow or fabric panels a
side; a conventional tail on the boom's end or a T-tail; a 1 m or 2 m boom; tricycle gear (nose leg under the cockpit, mains on a cross axle from the spine half a
metre behind the centre of mass) or a tail wheel (mains under the cockpit, a short strut at the boom's end).

**Balance by building twice.** The wing's place is the one thing the balance moves: the plane is built off to one side with its wing at the first and last
spine station, `Construct.Com` and `Construct.LiftCentre` read from each (both move in a line with the wing), and the station that puts the centre of mass 0.2 m
ahead of the lift is built; a third trial gives the centre of mass the tricycle mains are hung behind. About 4 s a generation with the flight check.

**The flight check's plane mode** (`VehicleFlightCheck.RunPlane`, chosen when the stabilizer's keys are Plane): Shift down the runway (the flat floor) for at
most 20 s until 1.05 times `ConstructAero.StallSpeed` or off the ground by itself; S until 2 m up (3 s at most); the autopilot's altitude hold set 40 m up for
4 s (at least 5 m climbed), then set where it is: 2.5 s to settle, then 3 s in which its height may wander 8 m and its heading 8 degrees, with at most 12 degrees of
bank; then D for 3 s turns it at least 10 degrees right; never past 75 degrees of tilt; a plane may stand 16 degrees off level. The autopilot is engaged as
its panel would (`apMode`, `targetAlt`, `autopilot` settings): the hook bot pilots will use. Its line gives the stall speed, rotation speed and run, climb,
level drift, turn and the balance.

**State at the stop (2026-11-10): 1 of 12 seeds pass** (seed 3 passes the whole check). What the sweep refuses, in order of count:
- **Too little push.** At this game's gravity (18 m/s2) a 650-750 kg plane on one Three-Blade Prop (about 2,000-3,000 N, falling with speed) reaches its
  stall speed (26-34 m/s) only after 15-20 s, and tail-draggers sitting nose up never do. The Engine with a Two-Blade Prop was dropped for this. Next: two props
  on every single-engined plane too, a lighter airframe (fabric panels, no tank under a high wing), or bigger props; then the runway limit back to 15 s.
- **Rotation**: tricycles at their speed don't lift with S held 3 s: the mains' place behind the centre of mass is the lever to check first (the elevator's
  moment against the nose's weight), with the debug trace (`PLANE_DEBUG=1` prints the stations, centres and the run and rotation by the second; remove it
  once the generator passes).
- **Geometry refusals**: the Turbine's sides are flush with the cockpit block's (z-fighting: put a 0.5 m beam stub between them); a pusher's prop hits a fin
  or the seat (the pylon at the station clashes with a far-back wing: put the pylon behind the seat and in front of the fin by its own place); a T-tail fin
  on some booms won't stand level; the mains' spine socket may already hold the battery.
- **Level flight**: the autopilot's altitude hold on a plane overshoots (11 m in 3 s at 54 m/s): the check may need a longer settle, or the hold a plane gain.
- Not built: a jet variant (Turbojet, swept wings, which have no aileron sockets: elevons or ailerons on a straight outer panel).

Headless runs on the shared box: until main's start-up fix (268108ac) the parallel map load hung every command; `DOTNET_PROCESSOR_COUNT=1` was the way round.

## Next steps

1. **Fixed-wing prop plane generator** (started in phase 2, above; this was the plan): fuselage of beams or a lofted hull, wings from the Wings parts (straight, swept, fabric by faction),
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
