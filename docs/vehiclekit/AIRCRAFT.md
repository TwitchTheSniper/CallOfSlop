# Aircraft in the vehicle kit

The user (2026-11-10): the vehicle kit should generate small aircraft at run time: planes, drones, helicopters and more. Their users are AI factions
(spawned like ground vehicles, crewed by bots) and loot and capture (players find, steal and fly them). The player's own generator stays creative only.
The goal is the tools and systems that make aircraft at run time, not a few hand-made ones. Flight design: the project doc `claude/flight.md`, code guide:
docs/sandbox/CONSTRUCTS.md ("Air and lift", "Flight control", "Jets, rockets and repulsors", "Wings", "Propellers and rotors").

## In the game (2026-11-10)

What a player sees now, and how to get it:

- **Shipped seeds.** Every shipped style (Bandit, IT, Highland, Scam, British) names "Drone", "Plane" and "Helicopter" in its `Extra` list, with three seeds
  each in its `Seeds` (`Styles.cs`; a different three per style, all passing a 12-seed sweep): 45 committed blueprints in `Constructs/Blueprints`
  (`BanditDrone1.json` ...), made by `--gen-vehicle "<Style> <Shape>" --out Constructs/Blueprints` and covered by `--gen-vehicles`. Their files carry the style's
  group, so they sit with the faction's vehicles in the blueprint catalogue (Tab, Blueprints). A committed seed skips the flight gate when made again (it passed
  it when written); every other seed is flown first. Militia ships no files, as for its ground vehicles.
- **Creative spawn** (the parts menu, Blueprints tab, "Faction vehicles: new seed" on): an aircraft tile follows **"An aircraft tile makes one"**:
  - *on the ground* (the default): a new seed of the tile's style and shape stands 6 m in front of you (the same `CActKind.Vehicle` path as ground vehicles,
    gated by the flight check). Sit in a helicopter's or plane's seat and fly it on the stabilizer's keys (Shift climbs, Ctrl sinks, W A S D). A drone has a Drone
    Receiver, not a seat: fly it from a Remote Console on its channel. With "new seed" off the tile places the committed file.
  - *flying, piloted*: one of the tile's shape launched 20 m ahead of you at its height (drone 25 m, helicopter 35, plane 60) with a bot pilot patrolling about that point.
  - *faction air role* (scout, patrol, escort or any): the style picks the aircraft for the role through the faction hook below, flying with a pilot.
  Both flying choices are `CActKind.Aircraft` (protocol 230: u8 `AirAsk`, style, shape or role, seed), so a client asks as the host does. "By role" and
  "a convoy" stay ground choices: an aircraft tile with either set makes one on the ground.
- **Faction aircraft.** No runtime faction system spawns vehicles yet (convoys and roles come only from player or creative requests; StoryFactions has no
  spawner). The hook it will call is `Game.SandboxRequestAir(style, role, over, seed?, order, radius, yaw, done, owner)` (`GameSandboxAircraft.cs`): an air role
  (`AircraftRoles.cs`: scout mostly drones, patrol planes and helicopters, escort helicopters and drones; only shapes the style names) becomes a shape and a
  seed, made at spawn time, launched over the point and flown by an `AircraftPilot`. At most `Game.MaxFactionAir` = 8 such aircraft live (pilots flying plus
  requests on the way; all pilots stay within `MaxPilots` = 32). Ground roles never pick an aircraft (their shape lists name none).
- **Convoy escorts.** A convoy request (creative "a convoy") gets a helicopter or drone escort patrolling over it one time in three (`AirEscortChance`, from the
  convoy's seed), never sooner than 45 s after the last (`AirEscortEvery`).
- **Capture.** A player who takes the seat of a piloted aircraft takes it over: the pilot lets go on its next look (`Driver` set) and the keys are theirs. A
  landed or parked aircraft is entered like any vehicle.

Tried by hand (a throwaway host + client probe over RakNet with 80 ms lag and 2 % loss, Proving Ground): the client's catalogue lists 327 blueprints, 45 of
them aircraft; a client's "on the ground" Bandit Helicopter stood 7.5 m ahead, level (0.1 degrees); the client sat in it, Sprint climbed 18.6 m in 3 s after
the wind-up, W moved it 4 m, its copy within 0.5 m; a client's "flying, piloted" IT Helicopter launched 35 m up with a pilot, the copy within 1.4 m; a faction
scout (a Highland Drone) through the role; two pilots cost 2.5 us a tick and allocated nothing; the client boarded the piloted helicopter in the air through
its seat (pilot let go, the captor climbed 11.7 m on Sprint); a Bandit convoy got a helicopter escort; ten more requests filled the cap of 8 and the rest
were refused. Not tried: a piloted plane spawned from the menu (only through `--bench-aircraft`), a drone flown from a Remote Console in the game, a client
capturing a drone, round worlds.

**Left:** armed aircraft (the gunsmith: guns on the wings or under the tank, wired to the stabilizer's `fire`), a plane's landing (a piloted plane circles home),
loot tables for parked and crashed aircraft (`Loot.json`'s `aircraft`, a Remote Console found with a drone), the faction AI calling `SandboxRequestAir`
(bases, patrols, escorts that follow a moving convoy), per-style aircraft slots (prop or jet, fabric or metal, drone family) in `FactionStyle`.

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

`Generators/Drone.cs`, shape "Drone", registered in `VehicleRegistry.Extras`; every shipped style names it (see "In the game"), and any style can
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

## The Plane generator (phases 2 and 3)

`Generators/Plane.cs`, shape "Plane", in `VehicleRegistry.Extras` (seeds ship per style: "In the game"): `--gen-vehicle "Bandit Plane" --seed 3`, `--sweep-vehicle "Bandit Plane" --seeds 30`.
The Light Plane's wiring on a cockpit Frame Block and a beam spine: a seat into a Flight Stabilizer on Plane keys, its `throttle` into the prop, `pitch` into the
elevators, `yaw` into the rudder, `left aileron` and `right aileron` into the outer panels' ailerons, `flaps` into flaps on the inner panels (half the seeds).
From the seed:
- **Power**: a tractor Turbine with a Three-Blade Prop on a 0.5 m beam neck ahead of the cockpit (half the seeds); a pusher Turbine on a 1 m pylon (prop set `pusher`),
  at the wing's station but at least spine socket 3 back so it clears the seat (a quarter); or **twin Light Turbojets** hung under the second wing panels, fed from the
  tank (a quarter: the jet variant). Twin props are gone: a construct drives shafts from one engine only (`Construct.Engine`, `BuildShaftFeeds`), so the second
  engine's prop never turned and the plane drove in circles on the runway.
- **Wing**: low (tank under the spine), mid (wings on the spine's sides) or high (tank on top) of three or four straight, narrow or fabric panels a side (four on a
  jet); a pusher is low or mid.
- **Tail**: on a 2 m boom (a 1 m boom put the tail plane in a far-back wing's flaps and a pusher's prop in the fin), a conventional tail or a T-tail; three tail
  planes side by side (6 m, three elevators), so the elevator lifts a tricycle's nose at take-off speed.
- **Gear**: tricycle (nose leg under the cockpit; mains on a cross axle from the first free spine or boom socket at least 0.25 m behind the centre of mass, clear of
  the tank and nose wheels) or a tail wheel (mains under the cockpit, a short strut at the boom's end). A tail-dragger sits up to 10 degrees nose up:
  `VehicleCheckOptions.MaxRestTilt` (8 for every other vehicle) is the flight check's 16 for planes.
- Control surfaces are put on level and facing forward (`Level`): fitted by the smart rotation, a flap stood on end and hit the mains' wheels.
- **The stabilizer's settings**: `strength` 200 (at the default its pitch lags the altitude hold by 10 degrees and the hold rocks 15 m), and a jet's
  `throttleCurve` 100 (24 kN at full throttle for the take-off, a third of it at a cruise throttle of 0.7; at full push it cruises past 60 m/s, where the hold rocks).

**Balance by building twice.** The wing's place is the one thing the balance moves: the plane is built off to one side with its wing at the first and last
spine station, `Construct.Com` and `Construct.LiftCentre` read from each (both move in a line with the wing), and the station that puts the centre of mass 0.2 m
ahead of the lift is built; a third trial gives the centre of mass the tricycle mains are hung behind.

**The flight check's plane mode** (`VehicleFlightCheck.RunPlane`, chosen when the stabilizer's keys are Plane): Shift down the runway (the flat floor) for at
most 20 s until 1.05 times `ConstructAero.StallSpeed` or off the ground by itself; S and Shift until 2 m up (3 s at most); the autopilot's altitude hold set 40 m
up for 4 s (at least 5 m climbed), then set where it is with Ctrl held 0.6 s (the throttle back to 0.7, as a pilot levels off): 5 s to settle, then 3 s in which
its height may wander 8 m and its heading 8 degrees, with at most 12 degrees of bank; then D for 3 s turns it at least 10 degrees right; never past 75 degrees of
tilt. The autopilot is engaged as its panel would (`apMode`, `targetAlt`, `autopilot` settings): the hook bot pilots will use.

**State (phase 3, 2026-11-10): `--sweep-vehicle "Bandit Plane" --seeds 60` passes 60 of 60, `"IT Plane"` 60 of 60.** The three phase-2 refusals were all
twin-jets on **fabric** wings: some 70 kg lighter for the same 24 kN, they climbed 1.7 m/s through the cruise throttle and the altitude hold rocked 10-17 m. A
jet's wings are now metal (a fabric pick becomes a Wing Segment; no other seed changes). The jets still hang under the second panels; with metal wings the hold
keeps within 8 m, so the thrust line was not the cause. Not verified: flying one by hand in the game, planes on a round world or in wind, and how long the fuel lasts.

## The Helicopter generator (phase 3)

`Generators/Helicopter.cs`, shape "Helicopter", in `VehicleRegistry.Extras` (seeds ship per style: "In the game"): `--gen-vehicle "Bandit Helicopter" --seed 7`, `--sweep-vehicle "Bandit Helicopter" --seeds 30`.
The Small Helicopter's wiring: a Turbine stood on end, a T Gearbox on it and a Helicopter Rotor on top; the gearbox's branch shaft to a Right-Angle Gearbox on a
short mast at the tail's end and a Tail Rotor on it (`torqueMix` 1, `reverse` by its side); a Flight Stabilizer on Rotorcraft keys wired `lift` to the
collective, `pitch` and `roll` to the cyclic, `yaw` to the tail rotor. A 2 m keel with the seat on its front (an open seat, or a cab of two frame blocks), a fuel
tank (or two) and the battery under the keel's front, then a 2 m and a 1 m boom and a 1, 1.5 or 2 m tail with the fin (and maybe small tail planes). Skids:
a cross beam under the mast, posts and two 2 m skids. About 800-890 kg; lift 1.3-1.4 times the weight.

- **Balance by building twice** (as the plane's wing): the mast (turbine, gearbox, rotor) goes on one of the keel's and boom's top sockets (stations 3 to 13).
  The craft is built off to one side with it at either end, the gap between the hub and the centre of mass read from each, and the station where it is least is
  built (within some 5 cm). The cyclic trims little: its 5 degrees and hub moment hold about 5 cm of offset at hover thrust, so a mast 0.5 m off hung it 10-25
  degrees nose up. Both are read in the body's frame (`PartBounds`, `Construct.Com`): `Vehicle.CentreOf` is in vehicle coordinates and doesn't compare with `Com`.
- **The tail rotor's push** holds the main rotor's torque (some 8.5 kN m at 340 kW and 380 rpm): on a 3 m arm about a sixth of the weight, so a helicopter
  hangs rolled 8-12 degrees against it in the hover and drifts about 2 m/s sideways. The boom is long and the tank and battery sit forward, so the mast comes
  forward and the arm is long. The stabilizer's lean (`maxTilt`) is 25 to 35 degrees, on top of that hang.
- **Wheels** are off for now: on wheels under the tank and a tail leg it stood 17 degrees nose up. They wait for mains placed by the balance and a tail wheel.

**Changes to the flight check** (`VehicleFlightCheck.Run`, rotorcraft): it waits, seated and hands off, up to 10 s (`SpoolTicks`) for every helicopter
rotor to reach nine tenths of its rated speed before the climb (its blades take some 4 s to wind up; drones' rotors don't count, so the drone check is
unchanged: Bandit Drone 30 of 30). A craft with a big helicopter rotor may hang up to 14 degrees in the hover (`MaxHeliHoverTilt`; drones keep 8). A hover
refusal now says the pitch and roll and the speed.

**State: `"Bandit Helicopter"` 30 of 30, `"IT Helicopter"` 30 of 30**, about 1.4 s a generation with the check (three builds). Seed 7: climbed 12 m in 2 s,
hover drift 0.3 m, hang 10 degrees at 2 m/s, 3 m forward on W, worst tilt 15. Not verified: flying one by hand, forward flight beyond 1.5 s, autorotation,
the fuel's endurance.

## Spawning and bot pilots (phase 4)

Code: `Source/VehicleKit/AircraftPilot.cs` (`AircraftAutopilot`, `AircraftPilot`), the server's side `Source/Game/GameSandboxAircraft.cs`, the hand probe
`--bench-aircraft [STYLE] [--seed N] [--height H] [--seconds S] [--map NAME]` (`Source/App/BenchAircraft.cs`; it prints to callofslop.log). Nothing new on the wire.

- **Ground spawn** works through the real runtime path: `Game.SandboxSpawnAircraft(style, shape, seed, over, 0, yaw)` (or `VehicleSpawn.Request` by shape) makes
  the seed in the generation thread's slices, gates it with the flight check (a refusal rerolls) and sets it down. Measured in a running offline Sandbox game on
  Proving Ground: Bandit Drone, Plane and Helicopter 7 all generated (no reroll, no fallback; the plane in 26 ticks, longest slice 16.6 ms on the busy box) and
  stood level (the tail-dragger plane at 6 degrees).
- **Air spawn**: `SandboxSpawnAircraft(..., height, yaw, order, radius)` sets it down, then in the same tick moves it straight up `height` m
  (`SandboxSpawnMove`) and `AircraftAutopilot.Launch`es it: every rotor and prop at its rated speed, the engine running at 95 % of its top rpm with a turbine fully
  spooled and `RotorRuns` set (without these a helicopter's engine stopped on the first tick, restarted, spooled from idle and sank 9 m in 1.5 s), the stabilizer
  armed with its autopilot holding the height (Hover; a plane: Waypoint ahead, throttle at cruise 0.7, going `max(25, 1.5 x stall)` m/s along its nose), home set
  there. Measured 40 m up: in the first 1.5 s the drone held within 0.8 m, the helicopter 1.2 m, the plane sank 1.9 m (no stall, 44-46 m/s).
- **Bot pilots** (`AircraftPilot`, server only, at most `Game.MaxPilots` = 32): orders `Hold`, `Patrol` (a rotorcraft flies the corners of a square of the
  radius about the point; a plane circles the point, which the stabilizer's waypoint mode does by itself when near), `Goto`, `Return` (home, then down) and `Land`
  (rotorcraft). It sets the autopilot through `AircraftAutopilot.Set` (mode, held height `Height` m over the higher of the ground under it and under the point,
  waypoint) every 15 ticks and costs a counter between; the ground is one ray through `World.Query` (constructs aren't in it), the last hit standing in off the
  map's ground. Landing steps the stabilizer's own held height (`CStab.HoldAlt`) down at 2.5 m/s over a fixed point with the landing assist at 4 m: the setting
  can't name a height of 0 or less (it reads as "where it is"), so ground below y = 0 was out of reach. On the ground and still it turns the autopilot off,
  disarms the stabilizer and lets go. It also lets go when the craft is gone or loses its stabilizer, and when a player takes a seat (`Driver` set: captured;
  the autopilot goes off so the keys are theirs).
  Measured (`--bench-aircraft`, 20 s patrol at 40 m, then return): pilots' ticks 1.2-1.8 us for three pilots, 0 bytes allocated over 1200 ticks; patrol heights
  within 5 m (drone), 3.4 m (helicopter) and 10.6 m (the plane, banked up to 48 degrees in its circle); the drone and helicopter came home and landed
  (the helicopter needs its ground speed under 3 m/s at home first, some seconds of hunting).
- `VehicleFlightCheck`'s autopilot now goes through `AircraftAutopilot.Set` (same settings as before).
- **Roles in the brief** (`AircraftAutopilot.RoleOf`, set by `VehicleBriefMake.Make` when the kit gave no role): "scout drone" / "gun drone", "gunship",
  "transport" (a rotorcraft with more than one seat), "light helicopter", "patrol plane" / "strike plane". A string the brief already saves; no format change.

Not verified: a client watching a launched or piloted craft (no wire change, but not looked at over RakNet), a player boarding a piloted craft (the let-go is
one comparison), a round world (the pilot's ground ray and square patrol are flat-map), a plane landing (it circles home), fuel and battery running out on a
long patrol, two pilots near each other (no avoidance), and Goto/Hold orders (only Patrol, Return and Land were flown).

## What comes next for AI aircraft

- **Faction style slots** (the other session owns `Styles.cs`): which aircraft a style fields (prop or jet, fabric or metal, drone family). The three shapes are
  in every shipped style's extras with seeds since 2026-11-10 ("In the game").
- **Who calls `SandboxRequestAir`**: the hook and convoy escorts exist ("In the game"); the faction AI (StoryFactions) doesn't call it yet.
- **Loot and capture**: a parked or landed generated aircraft with a Chest (`Loot.json`'s `aircraft` table), a Remote Console found with a drone (the receiver's
  channel as the key), and a captured craft staying the player's (the pilot already lets go when a seat is taken).
- **Armed aircraft** through VehicleGunsmith (guns on the wings or under the tank, wired to the stabilizer's `fire` as the Strike Plane is); a gunner or the
  pilot firing needs the gunner bots of the vehicle-weapons work, not this pilot.
- **Coaxial rotors** need a part: a rotor has only its base socket, so a second disc can't stack on one shaft.

## Next steps

1. **Jets**: the level hold passes with metal wings (phase 3); the thrust line is still under the centre of mass, so a heavier jet may want them on the spine's sides; swept wings (Large/Medium
   Swept Wing L/R have no aileron sockets: elevons from the tail planes, or ailerons on a straight outer panel), the afterburner on a wire for a dash, a
   Small Turbojet (75 kN) for heavier strike jets.
2. **Helicopters** (phase 3 made the conventional one): wheels (mains placed by the balance, a tail wheel); tandem (two rotors of opposite hands on one
   turbine's shafts, 9 m apart or overlapping at different heights, no tail rotor: the stabilizer yaws by differential cyclic); coaxial needs a part (a
   rotor has only its base socket, so a second disc can't stack on one shaft); a door gunner's seat; the Medium Helicopter Rotor for a 5 t lifter.
3. **Odd craft**: repulsor hover platforms, small airships on lift cells and burners, tilt-rotors (rotors on joints: the stabilizer doesn't map a rotor on a joint yet).
4. **Faction style slots**: aircraft slots in `FactionStyle` (which lifters, frames, skins, how big: prop or jet, fabric or metal wings), and each style naming
   "Drone" and "Plane" in its extras once its look is decided (then seeds ship and `--gen-vehicles` covers them). The other session owns `Styles.cs`.
5. **Spawning and bot pilots**: built (phase 4, above). Left: a plane's landing, round worlds, avoidance between pilots, the faction AI calling it.
6. **Loot and capture**: a parked or crashed generated aircraft with a Chest (`Loot.json`'s `aircraft` table), a Remote Console found with a drone, the
   receiver's channel as the key to steal one.
7. **Armed aircraft**: gun drones, strafing planes and bombers through VehicleGunsmith (guns on the wings or under the tank, wired to the stabilizer's `fire`
   as the Strike Plane is), and ConstructWeapons for the aim.
8. **Check gaps**: the flight check doesn't look at blade discs overlapping each other or the frame (the generator spaces them by its own sums), at
   endurance (battery minutes), or at a round world (it flies on a flat map, as the drive check drives on one).
9. **Patrol heights**: a plane banking 48 degrees round its point lost up to 10 m; a lower `MaxBank` for piloted planes, or a wider circle, would hold it.
