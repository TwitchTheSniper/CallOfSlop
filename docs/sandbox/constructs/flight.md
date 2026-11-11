# Flight

Covers air and lift, flight control, jets, rockets, repulsors, wings, propellers, the Fan and Airframe Formers, flight in survival, pilot safety. Part of the construct guide: index in ../CONSTRUCTS.md.

## Pilot safety

What a pilot wears is a part in the backpack (`"wear"`: section "Pilot gear"; `Source/PartKit/Generators/PilotParts.cs`), in a creative world everyone has all three. It is shared movement, predicted like the rest of `MoveState` (`Source/Physics/PilotGear.cs`), on the jump key in the air. **Parachute**: pressed falling (5 m/s down or more), sinks at 5.5 m/s, WASD steers a little; press again cuts away. **Backpack Glider**: pressed falling, 17 m/s and 3 m/s down; W dives (25, 7), S flares (10, 2.2), A and D turn at 70 deg/s. **Jetpack**: hold jump, 12 m/s up for 4 s; 12 s on the ground fills it. With several: the jetpack while it has fuel, else the glider, else the parachute. A landing, a deck, a seat or a ride frame takes it off; it opens only off a construct's frame, which a fall leaves on its own.

**Ejection Seat** (`"seat": {"eject": true}`): the key (`GameAction.SeatEject`, L) or a signal throws its rider 2.2 m clear at 24 m/s up, plus the craft's speed, out of every ride frame, and a parachute opens by itself after 0.75 s, backpack or not.

Protocol 176: the recipient's gear in its snapshot (flag 8), the others' canopy as a kind byte.

**Choices made**: there is no fall damage in the game, so these are for getting down and getting about, not surviving; the jetpack's fuel is its own and refills free; the jump key, not a new key, works them.

## Air and lift

Stage 1 of flight (the design is the project doc `claude/flight.md`). Code: `Source/Constructs/ConstructAir.cs` (the air, the one place flight reads it), `ConstructLift.cs` (cells, gas, burners, lift); numbers: `Constructs/Air.json`; parts: `Constructs/Parts/Air`, the demos `Hot Air Balloon`, `Scout Airship` and `Armoured Airship`, all from `Source/PartKit/Generators/AirParts.cs`.

**The air.** `ConstructAir.Density(p)` is 1 at sea level and falls by e every `scaleHeight` (1,200 m). On a planetoid it is 1 at the radius and falls the same way above it; in open space, curved gravity with no planetoid, it is 0 (`HasAir`) and nothing floats. `Wind(p)` is the weather's wind at the player, the low wind turning into the cloud-level wind by `windAloftAt`; with no weather on the map it is zero. `GroundEffect` and `Turbulence` are the hooks for later parts; turbulence already shakes cells near a player in a wind. Rounds' vacuum test goes through `HasAir`. The arcade flight drag keeps its old thinning (`DragShare`), since a planetoid's sky has drag but no lift: the climb out of the bubble flies as it did.

**A cell.** A Lift Cell is a Tank Volume with an `envelope` in its `fluidTank` block: the same box settings, handles, pipes and network, but the box is a solid: it collides, stops rays and rounds, and is drawn as a skin, slack on a soft cell until it fills. No Sealed volumes rules; 32 cells and 16 burners a construct. Sizes: 40, 80, 160 and 320 m a side by size class, soft (0.25 kg/m2) or armoured (1.6 kg/m2, armour 40). The skin weighs by its area, through the load path, at the box's centre. The gas weighs nothing of its own: it is in the lift figure.

| Gas | Lift, kg/m3 at sea level | With height | From |
| --- | --- | --- | --- |
| Hydrogen | 1.12 | as helium | pipe. Burns, and blows up |
| Helium | 1.04 | full lift until the gas has swelled to fill the bag (density = fill), then falls with the air | pipe |
| Hot air | 0.31 at full heat | by heat times the air | a burner under the cell |
| Vacuum | 1.225 | by emptiness times the air | a Vacuum Cell's own pump, on power |

The lift acts at each cell's centre, so a cell off the centre of mass tips the craft. A craft's ceiling is where lift meets weight.

**Softer past a size.** A construct's cells count `(volume / softFromM3) ^ softPower` times over, at most `softMost`: from 20,000 m3, 1.76x at 100,000 m3, 3.9x at 1,000,000 m3, 6x from about 3,300,000 m3 up. So a flying city doesn't need thousands of cells.

**Gas.** Pipes bring hydrogen and helium as they bring any fluid, 1 l of a tank's compressed gas a cubic metre (`gasLitresPerM3`), so a Large Fluid Tank's 150 l fills 150 m3. In survival the bulk materials `hydrogen` (a Smelter on dirt) and `helium` (a new underground deposit, tier 2 drill) go by chute into a **Gas Compressor**, whose pipe gives 50 l a unit. Creative sets the gas and fill on the panel. A cell's fill is a percentage as a tank's is, and for hot air it is the heat, for vacuum the emptiness. Resizing a cell keeps its percentage in creative; in survival it keeps its gas, so a bigger box is a slacker bag with the same lift and more skin. The look line says what it lifts of what the construct weighs, what it would lift full, and if it is frozen or has no air.

**Burners.** Role `burner`: `{"heat": m3 kept at full heat, "burnRate": l/s, "at", "reach"}`. It burns fuel as a thruster does (touching a tank, or a pipe), and heats the first cell up its flame, along the part's up. Its `throttle` wire sets it; its `height` wire holds that many metres over the ground with a slow trim; unwired, the driver's Sprint raises the setting and Crouch lowers it and then vents. Hot air cools at `coolShare` a second, by the air.

**Venting.** A cell's `vent` wire (0 to 1) lets gas go at `ventShare` a second (`ventHotShare` for hot air).

**Damage.** A hit that gets through the armour makes a hole by its damage (`holePerDamage`, at most `holeMost` m2 a hit), kept in the hidden setting `hole`. Gas leaves at `leakSpeed` m/s through the hole, as a share of what is in the cell. A cell's frame takes a tenth of the damage. A Vacuum Cell collapses at its first hole: wrecked, its lift gone. Fire lights hydrogen by the usual routes (burning rounds, flames, a neighbour), and so does any hit of `igniteDamage` or more: the gas burns away, and when the fire gets through the cell what is left goes up through the cook-off route, 4 to 40 m. Helium doesn't burn, and neither does the skin of a cell (`Flammable`). A wrecked cell has no box. **Patching:** the attach tool's Fire at a holed cell closes 0.5 m2 (times the part's size), for a unit of cloth in survival.

**The tick.** Every 15 ticks (`ConstructLift.Step`) the gas moves and each craft sums its lift. Each tick a free, awake craft's lift is applied (`ConstructPhysics.Lift`): near a player (`nearMetres`, `PlayerSpots`) cell by cell at the air at each cell, further off one summed force at the centre of lift. Then drag, 1/2 rho Cd A v2 against the wind, acting `dragArm` of the way from the centre of mass to the centre of lift (a stand-in for fins, or any ship would tip), and the air's hold on its turning. A sleeping craft costs one comparison every 15 ticks: a change of 0.5% of its mass in lift, or 1 m/s of wind, wakes it. The hover hold takes `LiftNow` off what its thrusters carry, and a winch takes it off what it hauls.

**The network.** `CBodyKind.Fluid` carries a hole with each tank (protocol 170), at 5 Hz while a cell's fill moves by more than 0.2%. `CActKind.PatchCell` is the patch.

**Mooring.** A rope to the ground holds a balloon, and it swings out on its rope in a wind and sleeps there. A craft rising on a slack rope jerks it: 7 t rising at 13 m/s snapped a 25 t rope, and an XL rope holds. Tie it short or fill it tied.

A cell has to lift itself: a soft cube needs about 3.3 m a side, an armoured one about 9.5 m (its block included).

**Choices made (the developer may change them):** gas has no weight of its own in a cell; a cell's box is a plain box until shaped envelopes come; a burner's reach is along the part's up, not a pipe; hot air and vacuum don't pipe; a hit always makes a hole, however small; dirt as hydrogen's source (nothing better exists to make it from yet).

## Flight control

Code: `Source/Constructs/ConstructFlight.cs` (the loops and the mix, in the logic step), `ConstructEffectors.cs` (what a stabilizer can command), `ConstructFlightPanel.cs` (the panels, the balance sums), `Source/App/Program.SandboxFlight.cs` (a pilot's finger, the markers), `PanelRender.FlightPlot`; parts: `Source/PartKit/Generators/FlightParts.cs`.

**A Flight Stabilizer** (small, medium, large, capital) is a logic part with built-in sensors and no need of power. Fit one, level with its arrow forward, and it flies what it finds. **No wiring and no setup:** when the structure changes it looks over its construct for everything it can command and works out, from where each sits and which way it points, the force and torque a unit of command gives. Thrusters, jets, nozzles' gimbals, repulsors, props and rotors with a helicopter's cyclic, control surfaces and its own gyro are commanded; burners and vents are worked by a slow height trim. Each tick it turns the seat's wishes into a demand of three forces and three torques and shares it among them all by least squares, then a few rounds putting back what limits took off, attitude first. Four jets in any spacing, six, a lopsided three, a rotor and a tail rotor, a centre of mass behind the mast: all fly on the same part. A dead or empty effector leaves the mix within half a second. A row nothing can answer is left out and named; a craft leans along any way it has no two-way push for. A rotor on a joint is left to its wires.

**The seat** is the one linked to its `seat` port, else the construct's first, helm first. A Drone Receiver is a seat. It stays at work with nobody at the controls and lets go when asked down and not going down for 0.6 s, or at zero throttle on plane or rocket keys.

**Several stabilizers:** the first flies and the rest stand by, and say so on the look line.

**A wire overrides the scan.** A thruster, rotor or surface with a wire into it from anything but its stabilizer is left to that wire. Wires from the stabilizer into a part it has found change nothing. Its named outputs still carry the same demands for anything wired by hand: `throttle`, `reverse`, `lift`, `vent`, `pitch`, `yaw`, `roll` (-1..1: nose up, nose right, right wing down), `left aileron`, `right aileron`, `flaps`, four corners, `strafe left`, `strafe right`, and `fire`, the pilot's fire key. Inputs: `seat`, axes that add to the seat's (`throttle`, `pitch`, `yaw`, `roll`, `lift`), `autopilot`, `mode` (1 to 5), `altitude`, `heading`, `waypoint x`, `waypoint z`, `set home`.

**Keys** (the `keys` setting; they are the seat's move and roll actions, so they rebind in the usual place, and only a stabilised craft reads them this way). Auto picks: wings and a push that is mostly forward make a plane, nozzles a rocket, anything else a rotorcraft. The old Helicopter, Quadcopter, Hover platform and Airship patterns are all Rotorcraft: the scan is what tells them apart (a craft with two-way jets stays level and slides; gas keeps a ship level).

| Keys | W, S | A, D | Q, E | Shift, Ctrl | Space |
| --- | --- | --- | --- | --- | --- |
| Rotorcraft | forward, back | yaw | roll (sideways) | climb, descend | |
| Plane | nose down, up | roll, the rudder follows | yaw | a held throttle, more, less | wheel brake |
| Rocket | lean on, back | lean left, right | spin | a held throttle, more, less | |

Invert pitch swaps W and S. **Settings:** keys, strength, stabilise level (off: the keys ask for turning), mouse aim (it flies where the seat looks), invert pitch, max lean, turn, climb and speed (0: their own), trim, and the autopilot's. It never leans further than its spare thrust can hold up, its loops go as fast as its slowest thruster follows (a helicopter's rotor winds up slowly, but its pitch answers at once), and a speed loop stays inside the attitude loop. A slow lean integral works off a steady side push.

**The gyro.** Thrusters have no torque about their own axis, so four pushing the same way can't turn. A stabilizer has a gyro (`logic.gyro`, N m: 1,500, 400,000, 30,000,000, 2,000,000,000), an effector like any other about its own up.

**Autopilot.** Hold altitude, hold heading, hover, waypoint, return home; on the configure panel, a Flight Control Panel or its inputs. Altitude 0 and heading -1 mean where it was when engaged. Home is where it last armed, or `set home`. A plane circles a point; a ship stops turning for it 25 m off.

**Flight Control Panel** (S, M, L, capital; the old Flight Panel by name): height, climb, speed, heading, attitude, the autopilot and how it is set, whether every axis is controlled, and buttons for AP on, off, next mode, set home, level, strength, hold this altitude and hold this heading. A pilot's finger reaches it from the seat.

**Flight Diagnostic Panel** (S, M, L): the craft from above and from the side, an arrow at each effector the way it pushes, long and bright by what it is told, blue or orange by the sign for surfaces and gimbals, red when dead or out of fuel, up to 24 arrows. It names any axis it can't control. It redraws at the panels' 4 Hz, and clients hear the commands at 5 Hz while it is at work (`CBodyKind.Flight`).

**To add a family of effectors:** a class with `IEffectorSource` (`List`, `Authority`, `Command`) and a line in `ConstructEffectors.Sources`.

**Balance markers.** With the attach, linking, configure or selector tool out, or a part in hand, the crosshair on a flyer shows the centre of mass (yellow), lift (blue) and thrust (orange), labelled with how far ahead of the centre of mass each is. `Construct.LiftCentre` is the one sum: a new lifting thing adds itself with `ConstructFlight.AddLift`.

## Jets, rockets and repulsors

Code: `Source/Constructs/ConstructBoost.cs`; parts and demos (Jet Hover Platform, Two-Stage Rocket, Hover Car, Anti-Grav Barge): `Source/PartKit/Generators/BoostParts.cs`. All but Anti-Grav and the decoupler are thrusters with a `kind`.

**Turbojets** (`"kind": "air"`): thrust by the air's density, a little more with speed, none below 3% of sea level or in vacuum, nothing out of the bubble. Fuel as any thruster. The old Light, Small, Medium, Large and Capital Jets stay as they are, working anywhere, since the Orbiter and Jumper climb out on them.

**Afterburner** (`"afterburner": {force, burn, band}`): its input on and the throttle in its band. Turbojets: 2.5 times the thrust, six times the fuel, lit in 0.6 s. No heat damage yet.

**Rockets** (`"kind": "rocket"`): fuel and oxidiser by pipe from Fluid Tanks (`Fluid.Oxidiser`; survival: a Smelter makes `oxidiser` from stone, and a Gas Compressor pipes it). Work anywhere. Propellant doesn't change their mass yet.

**Gimbals** (`"gimbal": degrees`): a nozzle's own, on its `gimbal pitch` and `gimbal yaw` inputs, or commanded by the scan however the nozzle was turned when fitted. A Motor Hinge gimbal still works by wire.

**Solid boosters** (`"kind": "solid"`, `burnTime`): lit once by their input, or Shift unwired; no throttle, and a stabilizer doesn't count them.

**Decoupler:** a rising input and it goes, and each side flies on as a working construct with its tanks, links and seats. The side with the drive seat keeps the body. Pools of fuel and charge split by capacity. Sides part at `kick` m/s by their masses.

**Repulsors** (`"kind": "repulsor"`, `range`): a push along the part's axis off the ground or a construct under it, exp(-4.6 gap / range) to none at its range, with a shock absorber per corner. On power. They look every third tick in turn. Height on a repulsor craft is the gap, so it follows the ground. Nothing pushes back on a construct underneath yet.

**Anti-Grav** (`logic.kind: antigrav`, `share`, `radius`): on power it takes `share` of the weight of what lies in its radius off the whole construct, so several add up, to at most all of it. Radius grows by the lift cells' soft scale on big ships. No power, and it stops that tick. Works wherever there is gravity.

### Link type from the port (at01)

Board item dev-link-auto-type-at01 (the developer, 2026-10-31; decision 20 amended). The linking tool has **no type choice**:
its outer ring holds only the modes (`SandboxLinkWheel`: Route links, Delete links, Move node, Re-route, Loom, Highlight all). The
type comes from the port.

- **Start.** Fire on a device opens the **port picker** with ALL of its ports at once (`ConstructRoutes.AllPorts`: every type,
  inputs and outputs, named ports then data in / out, then a storage part's chute), each by name with its type icon and colour
  (`SandboxLinkWheel.TypeColour`) and a hint line (type, in / out, the fluid, "(no effect yet)"). Ports a link can't start at
  (inputs, shaft sockets in use) are greyed with the reason. The usable port nearest the crosshair is **pre-highlighted**
  (`RadialMenu.Preselect`), so a click confirms it with no further aim; a one-port device (a seat's Data out) gets a
  one-option picker. Picking a port starts a link of **that port's type** with that type's rules, look and preview. While
  aiming at a device, every port is a labelled marker on the part in its type's colour (`Program.LinkPortMarkers`).
- **End.** While routing, a click on a device whose ports include one that takes the link opens its picker: the ports of the
  link's type that are compatible are enabled and the nearest is highlighted; every other port is greyed with its reason
  (`ConstructRoutes.EndWhy`: "a drive shaft can't connect to Fuel in", "Drive out is an output: a pipe ends at an input",
  "this pipe carries fuel, Coolant in takes coolant"). A device with no port of the link's type stays a surface for a node,
  and its markers are grey with the reason. The picker is always shown, even for a single matching port (np01's amendment).
  The picked port is checked with the server's rule (`ConstructRoutes.Check`) and a refusal is said, with the link kept.
- **Branching** off a link takes the link's type. Free routing (Shift), hold-and-scroll, world links and hoses are as before.
- **HUD.** The line names the link being made: "Drive shaft from Engine: Drive out, 2 nodes: ...".
- **Wire.** Nothing changes (a link already records its type); protocol stays 104.
- **Self-test.** **Link auto type** (`SelfTestLinkAutoType.cs`), through `Program.LinkTestFrame` (the linking tool's real per-frame
  input) with a host and a separate client: the ring has no type; an engine lists Fuel in, Drive out and Exhaust out together
  (a fuel inlet greyed at the start); a transmission's Output shaft starts a drive shaft that ends on the differential's
  Input shaft (output shafts greyed) by one confirming click; a tank's Fuel out starts a pipe ending on Fuel in (Drive out greyed
  "a pipe can't connect to Drive out"); a fuel inlet's reason for a shaft; branching off a pipe makes a pipe; a seat's one-option
  picker; a lamp's end picker (Power in greyed for a data cable); delete and re-route; the client mirrors the links.

### Self-test

The **Construct routed links** section (`Source/App/SelfTestConstructRoutes.cs`): each type's bend rule and the snap
keeping it, nodes snapped off a surface never inside it, a sunk node, a segment through a part and wrong ends refused;
a switch lights two lamps through a data junction, a cut branch and its repair; a power cable carrying the circuit past a
wrecked tank, its junction splitting evenly, cut and sparking; fuel reaching two engines through a pipe junction, a cut
branch starving one and leaking; the buggy's fuel pipe
cut and repaired; the migrated truck's shafts driving four wheels (two with the front shaft cut), a bare shaft junction
and a shaft corner refused; a chute junction sharing a bin's metal evenly; links following a driving buggy and a turning turret;
blueprint `"links"` and `CBody` round trips (a short packet refused); the server's linking tool checks; the migrations;
300 part files with their own ids; no allocation in 120 ticks of three constructs with links, nor in rays at a cable.

**Link clipping grace** (`Source/App/SelfTestLinkRouting.cs`): a pipe from a tank to an engine on the slab's edge,
its end segments rising from below the slab's side over its corner, passes within the grace where the old rule refused
it, and a segment through the slab past the grace is still refused; a pipe's nodes 50% sunk into the slab pass,
70% fail; an outside turn over a frame block's edge passes (the old rule refused it); a node clicked into a wall: the
adjust search finds +1 cm, one notch moves 1 cm and Ctrl 5 cm, the range is clamped; the preview and the server agree.
**Link free routing**: with free routing a click on an engine (an input of pipes) places a node there and the link stays
open, without it the click ends the link; a pipe routed along that engine to another is accepted and feeds only the
other; the key is Left Shift beside Sprint without a clash.

### Choices made (the developer may change them)

1. Tunable constants in `ConstructRoutes`: pipe 15 degree steps, chute 45 degree steps (developer), shaft at most 45
   degrees a bend (developer), shaft junctions need a T gearbox (developer); 5 cm node grid; 8 m a segment and no limit on a whole link (user, 2026-11-04, was 24 m and 32 nodes);
   segment health 20/40/80/250/80; a cut fuel pipe leaks 0.5 l/s; a chute moves a load (10 bulk) or one part a second.
2. Picking a link type: none any more (board item dev-link-auto-type-at01): the port clicked gives it. The outer ring keeps the modes.
3. Power cables add to the frame circuit (the frame still carries power, so every earlier construct works); a cable
   carries the circuit both ways.
4. A shaft link is a drive-line edge with no ratio (each U joint keeps 99%); chain and belt drives stay devices.
5. A link with no nodes is checked like any other (it may not pass through a part); migrated wires and blueprint links
   with no nodes are straight and checked for reach only.
6. Repairs are instant with the attach tool; junction ratios are even (the configure tool can make them
   settable later).
7. (lg01, ls01) The grace counts from where a link leaves its device's boxes, not from the part's centre; the adjust
   search goes out first, then in; the input end's adjust moves the last node (a link with no nodes can't be adjusted:
   its reason is said); the adjust's big step is Ctrl (which is also Crouch: holding it crouches); Free routing shares
   Left Shift with Sprint (holding it while routing also sprints).

## Wings

`ConstructAero.cs`, data in part files and `Constructs/Air.json`; generator: the part kit's `--gen-parts wing_parts`. Every lifting surface runs through one place, reading `ConstructAir` for density and wind.

A surface is any part with a `"surface"` block, so a new one is data: `area`, `at` (where it acts, the quarter chord on a wing), `lift` and `forward` (directions in the part), `aspect` and `sweep` (they set the lift slope and induced drag), `cl0`, `stall` (degrees), `cd0`, `tears` (cloth: lift goes with its health), and `control` for a surface with a hinge in the part.

- **Near a player** (`Air.json` `nearMetres`, `Construct.LiftNear`, worked out four times a second) each surface finds its own velocity through the air, its own angle of attack, its lift and drag by that angle and pushes at its own centre: a straight line to the stall, a blend over 10 degrees to a flat plate past it, at any angle, flying backwards too. A badly balanced plane tumbles. Its frame is its segment's, so a wing on an ordinary hinge or joint flies by its real pose.
- **Further off** the surfaces that lift work as one through the centre of mass, with no moment; it costs one surface. **Asleep** it costs nothing. A flying craft doesn't sleep while a rotor turns.
- A craft with surfaces has real drag: `bodyDrag` for everything that isn't wing, and the arcade drag and speed cap (`ConstructPhysics.MaxSpeed`) stop applying while the air slows it (`Construct.AeroFlying`, up to `flightTop`).
- The ground's cushion (`ConstructAir.GroundEffect`) reads one ray a craft at 4 Hz. Past `softWingM2` of wing lift scales kindly.
- `ConstructAero.StallSpeed` is what the HUD and the look line use. `Construct.LiftCentre` counts every surface that lifts, the tail too, by its lift slope at 40 m/s: the point the craft balances round, so the centre of mass goes ahead of it. `ThrustCentre` counts rotors.
- On a Flight Stabilizer S doesn't brake on the ground (it is the elevator as a plane rotates); Space does.

**Control surfaces** (Aileron, Elevator, Rudder, Flap) take a data input, -1 to 1 of their `max` degrees, plus their Reverse and Trim settings. Only their wire moves them. They are made to take a Flight Stabilizer's outputs with nothing in between, on the normal layout: `pitch` into an Elevator behind the centre of mass (plus lifts the nose: `"sign": -1` in its control block), `yaw` into a Rudder behind it standing up (plus swings the nose right), `left aileron` and `right aileron` into an Aileron each (plus is trailing edge down on either side), `flaps` into a Flap. A canard, a fin hanging down or anything else the other way round takes the part's Reverse setting. The leaf is the box tagged `"side": "leaf"`, drawn turning about `hinge`; the part's solid boxes don't move. Angles ride the pose (`ConstructNet.WriteAir`).

**Extruded airfoils**: the Extruder's Profile list gains the rows of `Extrusion.json`'s `"airfoils"` after Pipe. They are ordinary sections on the grid (wire, saves and blueprints need nothing new); a finished piece whose mask is one of them gets a `Surface` from its chord, length and row. Chord across x with the leading edge at -x, span along its length.

### Round worlds

Everything in flight reads up from the gravity where the craft is: `ConstructPhysics.BodyUp` (`Construct.UpNow`; world +Y on a flat map). It follows a smooth field exactly, turns at most 2 rad/s over a step (a zone's wall, a hollow world's cavity), and where gravity all but vanishes it keeps the last good up. Speeds and the way to a point are taken along the ground under that up (`ConstructFlight.Along`), never from world x and z. A stabilizer's headings count from a north it carries with it (`CStab.North`), so a held heading is a great circle and no pole is in the way; a heading typed in is on the compass from that moment. A home or waypoint is a whole point on a round world (`homeY`, `waypointY`). Altitude, heading and tilt sensors, wind, ground effect, burner height holds and a seat's aim follow the same up.

### Tuning

Every control surface and nozzle has **Throw** (10 to 300% of its full deflection, stopped at 43 degrees; `ConstructAero.ThrowOf`, and the stabilizer's scan counts it), **Expo** and **Reverse**.

The stabilizer's panel keeps its plain settings on top (a new **Preset**: Normal, Docile, Sport, which is what auto means: rates, responses and limits). Under the **Advanced** fold (a config entry's `"advanced": true`, its own group each) Pitch, Roll and Yaw have Rate limit, Response (P), Damping (D), Hold (I) and Expo, and Climb all but the rate, which is Max climb and Max descent; then Throttle curve, Max bank, Max pitch, Turn coordination, Auto-level, Wind hold, Landing assist, Collective range and Thrust limit. Zero is auto everywhere (Landing assist: off), gains are per cent of auto, so what the scan worked out stays the starting point. Expo shapes a wired stick: a key is all or nothing. A Flight Diagnostic Panel's last line is each loop's error now: pitch and roll degrees, yaw degrees a second, climb m/s.

Settings past the first 19 live in an array a part makes when one is set (`CfgValues`, `ConstructConfig.NearSlots`, `MaxSlots` 64), so each part grew by one reference, not 45 floats; copy a part's values with `Copy`.

The speed loop's drift integral is `k * k / 5 / g` for a speed gain `k`, and a drift of over 3 m/s counts as 3. It was ten times that, which with the attitude's lag made a limit cycle over the ground.

## Propellers and rotors

`ConstructRotor.cs`. A part of role `rotor` with a `"rotor"` block: `radius`, `ratedKw`, `ratedRpm`, `blades`, `bladeHealth`, `motorKw`, `shaft`, `heli`, `duct`, `topSpeed`, `spool`, `cyclic`, `hubMoment`, `at`, `axis` (the way it pushes), `forward`.

- **Power.** A rotor with a "Drive in" port is a shaft feed like a drill: bolt it to an engine's Drive out or a gearbox's output, or link a shaft to it. Its hub is geared so it reaches its rated speed at the engine's peak (`CRotor.Reduction`), and the engine is held there (`Construct.RpmGovern`). What it takes comes off the crank (`Construct.RotorLoad`, through `ConstructDrillDrive.Carry`), so an engine it overloads is pulled down. Unwired, the driver's Shift and Ctrl set a trim (`Construct.AeroTrim`) that is the engine's throttle. A rotor with `motorKw` and no shaft linked runs off the circuit (`MotorWork`).
- **Push.** It takes power by the cube of its speed and the air's density. Momentum theory turns that into push: T = 2 rho A vi U, P = T (V + vi), with `rotorFigure` of the power doing the work, `rotorProfile` of it pushing nothing, and fading to nothing at `topSpeed`. Forward speed helps a rotor up to a stall limit.
- **Torque** goes into the craft about its axis, by its Hand setting. A Tail Rotor's Torque mix setting adds the collective that cancels the others' torque, as a helicopter's pedals are linked to its collective.
- **Helicopter rotors** (`"heli": true`): inputs collective (-1..1), cyclic pitch, cyclic roll, then the drive bus. Collective is the blade pitch: at a constant speed it sets the push, and the engine is asked for full throttle. Cyclic tips the push by `cyclic` degrees and adds `hubMoment`, plus being nose up and right wing down, so a Flight Stabilizer's `lift`, `pitch` and `roll` go straight in, and `yaw` into a tail rotor's collective (Reverse collective when it pushes to the craft's right). A turning head damps pitch and roll.
- **On a stabilizer** a rotor on `lift`, `throttle` or a corner port counts as a thruster (`ConstructFlight.MapRotor`): a wire asks for a share of its full push (`ConstructRotor.FullPush`), and on a corner its torque goes into the mix, so a quad's opposite hands give it yaw. A rotor on a joint isn't mapped yet.
- **For a scan of the body**: `ConstructRotor.Effector` and `ConstructAero.Effector` give each one's place, direction and most force (and a rotor's torque), in its segment's frame; its inputs are its part's `DataIn`.
- A wire into a rotor on the shaft runs the engine, with no seat (`Construct.RotorRuns`).
- **Tilting**: on a joint its frame turns with its segment. A shaft link may end on a rotor across a joint.
- **Blade count and style** are settings on the rotor (`fanBlades`, 2 to 16; `bladeStyle`), locked to creative: in survival they come off the Fan Former and the prop carries them in its key (`Extrusion.FanWall`). 16 is the cap because blades lost and their wear are kept in 16-bit mask settings. `Air.json`'s `"bladeStyles"` is data: `push`, `power` and `top` scale the rotor, four `stations` shape the blade. More blades than the rotor's `BaseBlades` take more power (count ratio to the 0.3) and push harder (to the 0.1). A file rotor's mass doesn't change with its count.
- **Blades** are one box (`"spin": true`) the renderer draws as the style's four stations round the hub (one box each past 25 radii, so 16 blades stay 17 draws far off); the blur disc follows the tips' reach and how much of the disc the blades fill; hidden arms are what it collides with. A ray meets the blades themselves (`ConstructRotor.Ray`): the blade as it lies when stopped, a blur when turning, where `rotorHit` of the disc counts as blade. Each blade's health is four bits of wear in hidden mask settings (`Blade0`..`Blade3`, four blades each, 16 steps of `bladeHealth`, and `BladeLost`), so it rides saves and CBody and no tick reads it; every hit counts at least a step. A blade lost rebuilds the rotor with less push, shakes the craft (`rotorShake`, by the share of blades gone) and throws a blade of its size and style off as a wreck (`ExShape.Blade`). The Loose Blade part files stay only for wrecks already saved. A blast or a crash picks a blade; a ray at the hub hits the hub.
- A rotor's speed rides the pose as a share of its rated speed; each peer turns its own blades from that.

## The Fan Former

A Wheel Former whose piece is a whole rotor (`storage.wheel.fan`): `ExShape.Fan`, kept in `ConstructExtruder.GrowWhole`. Its settings say what it makes (`Extrusion.json`'s `"fanKinds"`), how many blades, which blade style and how far across. Fan Formers go 3, 12, 48 and 96 m by size class: the hub stands high enough for the prop it makes. The prop grows from the machine's first size, paid by volume from `Extrusion.json`'s `"fans"`, and parts off by itself at the size asked; a Cut signal parts it off early. What it can take and how fast it turns follow from its size by the kind's row. In survival this is where props come from: rotor parts get no Assembler recipe (the part kit's `assembling`); creative has the fixed sizes in the Tab menu.

## The Airframe Former

A Wheel Former whose piece is a wing or a control surface (`storage.wheel.airframe`): `ExShape.Wing`, grown by `ConstructExtruder.GrowWhole` beside the Fan Former's. Four sizes (`AIRFRAME_FORMERS` in `tools/constructs/extrusion_parts.py`): 6, 24, 96 and 192 m of span, 3, 8, 24 and 32 m of chord. Its settings say what it cuts (`Extrusion.json`'s `"airframes"`: wing, tail plane, fin, swept left and right, aileron, elevator, rudder, flap), the chord and the span. What is in its bin is the skin (`"wings"`: cloth, wood, planks, laminate, metal, steel, in units a m3 of skin). The chord is set when a piece starts; the piece grows along its span out of the machine, and parts off by itself at the span asked, or on a Cut signal. It takes a Run wire, as the rest.

- **The piece** is a run-time def by key, on the wire and in a blueprint: `Extrusion wing-aileron 50x0x200 cloth` is chord x 0 x span. Its span is along +z, its chord across x with the leading edge at -x, lift +y, as an extruded airfoil. Numbers come from its row and do not depend on the material, except that cloth tears (`SurfaceDef.Tears`, `PartBreak.Disable`) and has 1.6 times the drag. A 1.5 by 2 m metal wing weighs what the Wing Segment file does (27 kg); skin thickens with the root of the chord over 1.5 m.
- **Fitting.** Sockets are an extrusion's: `base`, `tip` and four faces at each metre of span, of the size class its chord gives. A wing is the same from either end: a left wing welds by `base`, a right one by `tip`, so the same piece makes both. Swept wings can't do that, so left and right are two rows. A control surface hinges along its leading edge: weld its `-x0` to a wing's `+x` socket. It has Reverse and Trim and an `angle` input, and a stabilizer finds it like any other.

## Flight in survival

Flight parts in survival are looted or made on machines; they are not gated by materials. Creative keeps every part in the menu. Generators: the part kit's `--gen-parts assembling`, `machine_tiers`, and the part kit's `extrusion_parts` and `air_cargo` (docs/sandbox/PARTKIT.md).

| What | Where it comes from |
| --- | --- |
| Wings, tails, control surfaces | An Airframe Former. The part files in `Parts/Wings` have no recipe: they come from wrecks |
| Props, rotors, ducted fans | A Fan Former (the part files have no recipe) |
| Jets, lift fans, rockets (their gimbal is a setting), boosters, decouplers, repulsors, anti-grav | A **Thruster Assembler**, from Turbine Stages, Thrust Chambers or Field Coils the plain Assembler makes |
| Cloth lift cells | A **Tailor**. Armoured and vacuum cells stay on the plain Assembler. Cells are boxes: shaped envelopes aren't made |

- **Assembler families.** A recipe's `"machine"` (`AssemblingTable.Machines`) gives it to one family, and a part's `storage.assemble.machine` says which it is: each lists only its own recipes (`ByMachine`, `ChoicesBy`), its Recipe setting an index into that list, and an old blueprint's name for a recipe on the wrong family is refused. Both have the Assembler's five tiers.
- **Anti-grav.** Anti-Grav Generators take Levite Cores, which take levite: a `levite seam` (`Resources.json`), a rare underground kind worked by a tier 4 Drill Module and smelted at tier 4. It is `"deep"`: a drill bores its whole hole, for nothing, before it strikes it (`DrillShaft.Bored`). A world smaller than the kind's 900 m cell may have none: Proving Ground places one.
- **Loot.** `Constructs/Loot.json` has an `aircraft` table, and a few flight parts in the others. A part with a surface or a rotor shows its tier by its size class, as it has no recipe. Crashed Plane and Crashed Jet are blueprints of real parts with a looted Chest: anyone may salvage them. Proving Ground has one of each; the world has no generator of wrecks.
- **Tiers.** With every part from its machine: the Hot Air Balloon needs tier 2, the Fabric Flyer (cloth, a wooden prop, an Engine) tier 3 for its stabilizer and battery, the Sky Crane tier 4 for its Battery Packs.

### Air cargo

A craft lifts any body with a Tow Eye. What is new is one part, the **Cargo Winch 16 m** (`"winch": {"grab": true}`): a winch a pilot works from the seat, with three inputs. A rising `grab` takes the nearest eye in its chain's reach, or lets go (`ConstructWorld.WinchGrab`); `reel` is a winch's own; `pay out` lets it down. Everything else is what parts already do: a coupling's chain, a winch's rating, a Cargo Volume's frozen box, a Drill Module on a body of its own.

- **Sky Crane**: four 2 m ducted fans on a hub, with every weight paired across it and the winch under its middle; 595 kg, and it has lifted 1.6 t all told at this game's gravity. Fire grabs, aim reels in, reload pays out. Its Flight Control Panel's autopilot (Hover) holds it over a load. Two Battery Packs give it about 2.5 minutes.
- **Cargo Pod**: a frozen Cargo Volume under a Tow Eye. **Drill Pod**: a Drill Module, a Battery and a Materials Bin under a Tow Eye: set it on a deposit, wait, lift it out. Ore is heavy (4 kg a unit): lift it before the bin outweighs the crane.
