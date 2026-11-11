# Big systems

Covers doors and event doors, large systems (motherships, carriers), carrier work. Part of the construct guide: index in ../CONSTRUCTS.md.

## Doors

A door is whatever hangs on a hinge, not a kind of part. F on any part on a passive hinge's leaf, or a motor hinge no wire runs, opens or shuts it (`ConstructEdits.Use`), so a door can be any size and any material. `Source/Constructs/ConstructDoor.cs` holds what the player sees of that; the parts come from `Source/PartKit/Generators/DoorParts.cs`.

- **Hanging anything.** A joint's leaf takes a door's `leaf` socket or a plain `struct` socket (`ConstructBuild.KindsMate`, used by the wrench's Place, the snap build, the socket weld and the vehicle kit), on a passive hinge as on a motor hinge. So a Panel, a plate or an Extruder piece goes on any hinge: an item with the wrench's path, a physics-only piece by a socket, point or freeform weld onto the leaf. An Extruder plate has edge sockets only at its stations (about every metre), so to hang one centred on its hinge use a length with a station at the middle (an odd number of metres) or a freeform weld.
- **The Door Hinge** (0.5 m) and **Long Door Hinge** (2 m) fit in a door's 2 cm gap: base socket on one jamb, leaf socket facing the door. The pivot is on the door's outer face (2 cm off the hinge's middle, towards its own -z), so the door swings out past the jamb and only the knuckles, 1 cm proud, show outside. They open one way: limits 0 to 100 degrees, towards the hinge's -z; turn the hinge end over to swing the other way. A player walking into a shut door from that side swings it, and from the other side is held. The old **Hinge** stays for what is built on it: 20 cm wide and proud of a wall, it suits gates.
- **The flush doors** (Steel and Wood, 4 cm thick) are named for their opening: a Flush Door 1.5x2.25 m is 2 cm smaller at each edge, with the Door Hinge in the gap on one side. The openings: 1.5x2.25 m a Door Wall, Door Corner or bunker (their `hinge` sockets); 0.7x1.8 m a Door Frame 1x2 m (its jamb is off the socket grid, so its hinge goes on with a freeform weld); 1.25x2.35 m a vehicle kit room; 1x2 m and 1.25x2 m for openings a player cuts. Each hangs from the middle of its edge, so a hinge at the opening's mid-height centres it. Steel weighs 43 kg/m2, so only the small ones are items; wood is physics-only, as the 1.25 m wood door is. The Two-Storey House, Bunker and Small Outpost blueprints use them.
- **The handle.** The **Door Handle** is a small plain part with a `"use": true` grip; fit it to a panel and that is what glows. A panel with none glows a hand's width at the edge away from the hinge, halfway along the hinge (`ConstructDoor.UseArea`). Both are only what lights: as on a Door, F works anywhere on the leaf. A beam or a frame on an unwired hinge counts the same way; engines, seats and the like still glow whole.
- **Pushing it open.** A hinge's **Opens when pushed** setting (`pushable`, on by default; every passive hinge: Hinge, Door Hinge, Long Door Hinge, Gate Hinges) says whether a player walking into what hangs on it swings it. The configure tool aimed at a door opens its hinge's panel, because a part with no settings of its own that swings on a joint is set on that joint (`ConstructDoor.ConfigPart`). A plain panel has its own settings, so aim at the hinge for one of those. A wire into the hinge's **Push** port wins while it is connected: high (0.5 and up) lets a push swing it, low doesn't, whatever the setting says; take the wire off and the setting is back (`ConstructDoor.PushOpens`). Only the server reads it, in `ConstructWorld.PushJoint`. F, a hinge's Lock port and what a motor does are untouched, and off doesn't stop a wall or a vehicle hitting the door.
- **The look line** on any of these: `Steel Flush Door 1.25x2.35 m: shut - F: open` (`ConstructDoor.Look`).
- **Nothing on the wire changes.** `CJoint.Open` and the joint's angle already go with every body; a hung panel is an ordinary segment in saves and blueprints.

Choices made (the developer may change them): a 2 cm gap; one hinge per door (a Long Door Hinge looks like a run of knuckles, but it is one joint); doors open one way, with no way to open inwards but turning the hinge; F works on the whole leaf and the use area only lights; the Hinge's own leaf takes any panel too.

## Event doors

A door that is neither a joint nor a segment (user, 2026-11-10): a construct has only 255 joints, and every hinged door spent
one and a physics segment every tick, so the Titan Walker's interiors ran out with 41 doorways bare. An event door sits in the
body frame as an ordinary part, shut or open, and changes only when a player or a wire works it. Code:
`Source/Constructs/ConstructDoors.cs` (events, the step, settling, drawing, the wire) and `Construct.Doors.cs` (the list).

- **The parts** (`Source/PartKit/Generators/DoorParts.cs`, `Constructs/Parts/Doors/EventSteel` and `EventWood`). A door part with a
  `"door"` block is an event door: `"kind"` `swing` (a whole number of quarter turns, `"angle"` about `"axis"` through
  `"pivot"`, in the part's frame) or `slide` (`"travel"` metres along `"axis"`), `"seconds"` to open or shut, and `"hang"`, the
  socket it goes on by. That socket is a plain one, so the wrench puts it straight on a jamb (a Door Wall's or Door Corner's
  `hinge` socket) with no hinge part. The Steel and Wood **Swing Doors** (1x2, 1.25x2, 1.25x2.35 and 1.5x2.25 m) swing out
  past the jamb towards their own -z, as a Flush Door on a Door Hinge does, and keep the 2 cm gap. A **Double Door L** and
  **R** (1.5x2.25 m) hang on either jamb and meet in the middle on their `meet` sockets: linked there, F on either works both.
  The **Bulkhead Door** (1.25x2 and 1.5x2.25 m, steel, 6 cm) slides into the wall on its hang side, so keep its width of wall
  there. A hidden 2 cm strip in the gap carries each plain socket (a socket is on a face).
- **The list.** `Construct.Doors` (at most `MaxDoors`, 1,024, its own array) holds each door part on segment 0 and the parts
  that ride its leaf: anything linked to that door part alone, on a socket other than its hang socket (a Door Handle, a sign),
  at most 8. It is built from the special parts after a rebuild (`EnsureDoors`). What lasts lives in the door part: where it
  settled in the hidden setting `doorOpen` (so saves, blueprints and CBody carry it), and while it moves `CPart.State` (moving,
  heading open) and on the server `CPart.Count` (ticks left).
- **Events, not ticks.** F (`ConstructEdits.Use` hands an event door's part to `ConstructDoors.Use`) or an edge on a wired port
  starts it. The ports: **Open** (the part itself: a rise opens, a fall shuts), **Toggle** (a rise) and **Lock** (high: F is
  refused); the configure tool's **Locked** does the same as a held Lock. Wires work a locked door. `ConstructDoors.Step` runs
  after the logic step: a body with no moving and no wired door costs one compare, a wired door its port readings, and only a
  moving door counts down. 1,000 idle doors on 1,000 bodies: 10 us a tick, nothing allocated.
- **Settling.** At the end its parts are put at the new pose (whole quarter turns and centimetres, so they stay on the grid),
  and only what the leaf touches changes (`Construct.DoorPartMoved`): its boxes move in the collider (`ConstructCollider.MovePart`)
  and its bounds in the part tree (`Construct.TreeMove`), each found under the part's old bounds, the nodes over them refitted
  from their children; the trees keep their shape. No rebuild, no `RefreshShape`, no joint, no segment. The body's mass and
  inertia stay as built (a big body's mass sums follow the part, so a later edit or full refresh sees it where it is), and its
  bounds only grow. A door part the collider holds in another form (a hull, fused, merged) has the collider built again when
  next asked for, as before. Cost on the 6,322-part fitted Titan Walker: 0.03 ms a door (0.5 ms the first time, JIT), 224
  doors settling in one tick 1 to 2 ms all told; nothing allocated (before: 8 to 15 ms a door, a 29 ms tick for 198 at once).
  While it moves the leaf stays solid where it was: an opening door blocks until it is open, a shutting one lets a player
  through until it is shut. A door due to shut on a living player (`PlayerSpots`) opens again. Constructs in the doorway are
  not checked.
- **Drawing.** A door's parts are never in the merged meshes (`ConstructRenderBatch.MergeBoxes`): they are walked every frame,
  a moving leaf at `ConstructDoors.DrawPose` (the turn or slide from its settled pose, on `ConstructRender.Now`), so neither a
  start nor a settle re-plans or rebuilds a chunk. `DoorEpoch` goes up only when the door list is built again (after a rebuild).
  `Construct.DoorOf` and `MovingDoorOf` are one array read (`DoorOfListed`).
- **The wire** (protocol 226). `CBodyKind.Door` carries `(u16 body, u16 part, u8 flags)` each: bit 0 heading open, bit 1
  settled. A client draws a start from where it has the door and puts the parts where the server did on the settle; both go
  before the body digest, so a client has settled before it compares hashes. A joiner gets the settled pose in the Full.
- **Joint doors stay.** A panel on a Door Hinge (above) works as before, and old saves and blueprints load unchanged.

Choices made (the developer may change them): F works a door and its partner together; a lock stops F, not wires; the leaf is
solid at its old pose while it moves (no sweeping push); the bulkhead slides sideways into the wall rather than up.

## Large systems

What makes a mothership work: a carrier of about 300 m launching planes of about 15 m, or a traction city (a tracked land
mothership carrying a town) that lowers a drawbridge and sends its vehicles down it. The parts are written by
`tools/constructs/large_systems.py` into `Constructs/Parts/Systems` (90 files; run it again after changing a table,
`--check` compares), in the inventory's function categories, under the Medium, Large and Capital size filter (the part kit's size classes, `PartScale.cs`: a socket snaps only to
its own class) and, for small-class controls, the small size. Code: `ConstructDeck.cs` (decks and conveyors),
`ConstructLauncher.cs` (catapults and arrestors), `ConstructSystems.cs` (the file blocks), the dock rules in
`ConstructCargo.cs`, the winch in `ConstructLinks.cs`, the fire director in `ConstructWeapons.cs`.

**Decks (decision 30).** A raycast wheel or spring foot now stands on other constructs as it does on the map
(`ConstructPhysics.GroundRay`). The tyre works against the deck's motion at the contact (`ConstructRide.SurfaceVel`: the
body's, and its joints'), so a parked vehicle rides a moving, turning deck, a lift or a turntable with no special code,
and a driven one rolls off onto the ground at the speed it has. The deck takes the reaction (`ApplyDeckPushes`), and a
joint under the part stood on takes the weight (`Construct.DeckLoads`: it shows on the Load bar and counts against the
rating). A free body lighter than a quarter of the wheel's body isn't ground (a crate under a truck stays a hull contact),
and neither is something coupled to it. A moving joint wakes the sleeping bodies near it, since wheels hold them clear of
any contact.

| Family | Medium | Large | Capital | How it works |
| --- | --- | --- | --- | --- |
| Ramp Deck | 8x5 m | 20x20 m | 48x48 m | Hang it by its -x edge on a motor hinge. The plate sits on the middle of its cells' height, so its tip is thin; it lowers to the ground and vehicles drive down it. |
| Lift Platform | 5x5 m | 20x20 m | 48x48 m | The same deck with a pedestal: bolt it on a piston's top. |
| Hangar Door | 5x3 m | 20x12 m | 48x32 m | A panel: a piston's top on its edge socket slides it, or hang it on a motor hinge. |
| Arched Gate | 5x5 m | 20x20 m | 48x48 m | Pillars, a lintel and an arch; `jamb` sockets for hinges. |
| Gate Hinge, Gate Leaf | 2x3 m | 8x12 m | 32x32 m | A passive hinge and a door leaf: F swings it. |
| Catapult | 16 m | 32 m | 64 m | Logic kind `launch`. 60, 70 and 80 m/s off it; up to 20, 200 and 2,000 t. |
| Arrestor Gear | 16 m | 32 m | 64 m | Logic kind `arrest`. 45, 35 and 25 m/s2; the same masses. |
| Docking Clamp | 30 t | 300 t | 3,000 t | A `dock` on a cargo volume. |
| Piston | 4, 8 m, 300 kN | 16, 32 m, 5 MN | 32, 64 m, 200 MN | Rated like the small ones (side load, give, the configure panel). |
| Motor Hinge | 500 kN m, 30 deg/s | 50 MN m, 15 | 1 GN m, 8 | The drawbridge, the crane, the gun cradle. |
| Turntable | 500 kN m, 45 deg/s | 50 MN m, 25 | 1.5 GN m, 12 | Turrets and cranes. |
| Winch | 16 m, 30 t | 32 m, 300 t | 64 m, 3,000 t | A hook with a `"winch"`. |
| Lifting Eye, Crane Boom, Conveyor | yes | yes | yes | An eye for a load; a lattice jib or gantry beam; a belt at 2, 4 and 8 m/s. |
| Shell Magazine, Ammo Hoist | 2,000 rounds, 8 m | 400, 8 m | 200, 16 m | An ammo box and a feed chute of the class. |

Also: **Flak Cannon** (Medium), **Naval Gun 20 cm** and **Rocket Battery** (Large), **Naval Gun 40 cm** (Capital), heavy
guns with their own numbers in the `gun` block; **Magnetic Lock** (Small, 6 t) and **Magnetic Lock Medium** (60 t);
**Bridge Helm**, **Crew Station**, **Fire Director**, **Long Range Sensor** (Ship systems); **Deck Speed, Fuel and Charge
Dials** (Medium); **Floodlight** (Small, Medium, Large), **Hanging Lantern** (Small, Medium); **Catwalk 4 and 8 m**, **Ladder
4 and 8 m**, **Scaffold Tower 4 m**, **Bulkhead Door Frame**, **Sliding Door** (Medium). Blueprints: **Deck Lift**, **Naval
Turret**, **Harbour Crane**, **Gate Ramp**.

**Docks.** A docking clamp or magnetic lock is a cargo volume with a `"dock"` block: `{"side": longest box side, "settle":
m/s it catches under, "maxMass": kg, "charge": kW, "refuel": l/s, "chute": true}`. So a signal or F sets it, and what it
holds rides kinematically on every machine with no pose traffic ([Cargo volumes](parts-and-data.md#cargo-volumes)). Unlike a cargo volume it
holds a crewed vehicle, needs only the body's centre of mass in its box, and has no limit a construct. While it holds
something, ten times a second, it moves charge and fuel from this construct's batteries and tanks into the held one's,
and with `"chute"` one chute's load from a bin of the held body into a bin here. Its clamps are electric: a dead battery
lets go.

**Catapults and arrestors.** The part's `"launch"` block is `{"width", "height", "accel", "maxMass"}`: its zone is its
length (its bounds along z), that wide, and that high over its top. A catapult fires on a rising input or F: the body
in the zone, moving under 3 m/s against the deck (or docked: it is let go first), is pushed along the part's -z until its
centre of mass leaves the zone or it has its `speed` over the deck ([Carrier work](#carrier-work)). The ship takes the reaction, and an electrical ship pays 1 / 0.8 of the work from its
batteries (flat: the shot ends where it is). It puts out 1 while firing. Arrestor gear, while on (unwired: always), slows
what moves through its zone to 3 m/s against the deck. Set them flush in a deck: the rail is a cell wide, the zone is not.

**The winch** (`"hitch": {"kind": "hook", "chain": 32, "winch": {"min", "speed", "rating"}}`): F with the attach tool hooks an
eye in reach, the chain starting as long as the gap; a signal of 1 reels in, -1 pays out; it stalls on a body heavier than
its rating. Protocol 118: a coupling's length is a u16 of centimetres; a reeling chain is sent every 25 cm.

**The fire director.** Wire a seat into it (else the seat that drives) and it into any number of guns. A [Turret Module](joints-and-logic.md#control-modules) does the same for one turret and holds no fire; the director is for a ship's broadside. Those guns aim where that
seat's sitter looks (`Aim`, unwired joints only), hold fire until they bear within 8 degrees, and fire on the sitter's fire
key (the director's output is the trigger, one tick late like every wire).

**The helm.** `"seat": {"helm": true}`: when two seats each reach the drivetrain through a Driving Module, the helm drives. A helm with no module controls nothing, like any seat.

**Conveyors.** Logic kind `conveyor` with `"conveyor": {"speed", "dir"}`: a signal sets its speed (-1 to 1), or unwired F
runs it. Hull contacts and wheels on it get its surface speed. It doesn't carry players.

**A lift, a ramp and a gate** go together on any deck: a piston under a Lift Platform; a Motor Hinge on a deck's edge
socket with a Ramp Deck on its leaf, axis level (the Gate Ramp blueprint), its Lowest angle set for how high the deck is.
Keep ramps under about 15 degrees for a buggy: its nose grounds on anything steeper.

**Self-test**: "Large systems" and "Large systems network" (`Source/App/SelfTestLargeSystems.cs`).

**Choices made (the developer may change them)**: wheels stand on a free body of at least a quarter of their body's mass;
the plate of a deck sits mid-cell, with sockets on its edges and four on top at the plate's face (off the lattice by the
plate's half thickness); catapults push the body directly, with no shuttle; a catapult's and arrestor's reaction goes on
the body, not through a joint; the arrestor needs no hook; docks feed one way, and unload bins with no filter; a winch has
no chain tension limit past its static rating; a conveyor is one logic part with no belt animation; a director's guns only
hold fire on unwired joints' aim. The blueprints carry no wires or small controls: fit a switch through a size-class adapter, or use F on a part (a piston or hinge with nothing wired into it opens and shuts on F).

## Carrier work

Fun first, forgiving, close to a carrier only in concept. The parts are in "Launch and docking"; generators `tools/constructs/large_systems.py` and `Source/PartKit/Generators/PilotParts.cs`; code `ConstructCarrier.cs`, `ConstructLauncher.cs`. The demos: **Carrier Deck** (a flying deck: arrestor and catapult in line, clamp, bay, mast; keep the runway clear, things stand at its edges) and **Deck Plane** (a Light Plane with an Ejection Seat).

**The pilot's key** (`GameAction.SeatHook`, G; `ConstructParts.KeyHook`): on a rail it hooks, then fires; held by a clamp or bay it lets go; with a clamp reaching for you it says no for 5 s. The seat's line says which.

| Part | Sizes | What it does |
| --- | --- | --- |
| Launch Rail, Catapult | 8 m, 6 t, 55 m/s; 16 m, 20 t, 60; 32 m, 200 t, 70; 64 m, 2,000 t, 80 | Roll on, moving under 8 m/s against the deck, press: the craft is held moving with the deck, slid to the rail's back end and lined up. Press again, or a rising signal or F: off at that speed over the deck (`launch.speed`), so a ship under way adds its own. The ship takes the reaction. A signal or F still fires anything settled on it, unhooked. |
| Arrestor Strip, Arrestor Gear | 8 m, 60 m/s2; 16 m, 45; 32 m, 35; 64 m, 25 | Slows whatever moves through its zone to 3 m/s against the deck, and calms its spin. No hook. Its zone reaches `launch.reach` past each end. Under 3 m/s a craft goes free for 3 s, so it can go round again, and a launched craft is left alone for 2 s. |
| Docking Clamp | S to Capital | Armed (F or a signal), `dock.pull` metres past its box it picks the nearest slow craft (under 15 m/s, with a seat or thrusters), draws it in at up to 8 m/s, upright, and holds it as cargo over the middle of its box: refuels, charges, the pilot's key lets go. Works with both in the air: the ship takes the reaction. |
| Hangar Bay | S, M, L | A dock with room and no tractor; starts holding. What parks in its box rides with the ship, crew and all. Signal off, F or the pilot's key: out along its front at `dock.eject` m/s. Doors are yours: hang a Hangar Door on a piston, on the same signal. |
| Mooring Mast | S, M | On (unwired: always) it ropes the nearest balloon (a body with lift cells) within `mooring.reach` of its top, moving under 8 m/s, as a real rope. Off or F casts it off. |

An armed Flight Stabilizer keeps its construct's surfaces on the near path (`Construct.LiftNear`), with no player about: an autopilot plane steers wherever it is.

**Choices made (the developer may change them)**: rails hold by setting the craft's velocity, not through a joint; the pull is firm enough to beat a stabilizer holding station; the demo deck has no batteries, so its rails are free and it refuels but doesn't charge; a craft's own throttle stays with its pilot on a shot.
