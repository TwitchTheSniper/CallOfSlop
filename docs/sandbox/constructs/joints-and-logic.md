# Joints, logic and control

Covers actuators, linear and flexible joints, mechanisms, control modules, screens and panels, cameras, drones, robot shells. Part of the construct guide: index in ../CONSTRUCTS.md.

## Machine screens

Production machines have a screen on their own model, source `machine` (`Source/Constructs/MachineScreens.cs`): a dark plate that `tools/constructs/screens.py` places on each model, on the flattest free spot near reading height, clear of sockets, ports and dies, since tiers and sizes differ. Every tier of the Smelter, Sawmill, Foundry and Assembler; every size of the Extruder and Wheel Former; the Recycler (`Constructs/Parts/Storage/Recycler.json`, by hand). The Press and Die Lathe have their own bay screens; gathering modules have none: they work on arms out of sight, and the look line says enough.

| Line | Refiner | Assembler | Extruder, Wheel Former | Recycler |
| --- | --- | --- | --- | --- |
| Top, in the state's colour | Refining, Needs input, Needs more, Needs tier N, Wrong input, Empty output, Output full, No power, Held off | Building, No recipe, Needs input, Needs tier N, Tray full, Output blocked, World is full, No power, Held off | Extruding, Forming, Waiting, Needs material, Wrong material, Other material, Bad section, At full size, Blocked, Path too heavy, Chute waits, No power, Held off | Recycling, Waiting, Output full, No power, Held off |
| Second | what it makes, or a batch | the recipe | the section, or the mode | what it is doing |
| Third | input | what it lacks | stock | output |
| Fourth | output | tray | the piece's length or size | |
| Bar | output fullness (a batch is about a second) | the job | the piece's length, else stock | the job |

Everything comes from the store and the part, so a client shows what the host does. A job's progress is new on the wire (protocol 155): `CStore.Progress`, sent about once a second while it runs and at once when it stops (`MachineScreens.SetProgress`), and only when above 0. Range 30 m, filled at 4 Hz in view, laid out again only when the card changes; a fill allocates nothing. `ink` colours them too.

## Display panels

Flat lit surfaces on a part, and a small card API for what they show (the user: "these UI screens will be utilized in a lot of different parts, so make it modular and make it its own thing"). Code: `Source/Constructs/DisplayPanel.cs` (the data and the card), `Source/Render/PanelRender.cs` (the drawing).

- **In a part file.** `"panels": [{ "source": "press.bay", "slot": 0, "at": [x, y, z], "size": [w, h], "face": "-z", "up": "+y", "rows": 5, "range": 25 }]`, up to 8. `at` stands a few mm off the face behind it (no coplanar faces). `rows` lays the text out, 1 to 6; `range` defaults to 400 x a row's height, 12 to 120 m. `source` names a `PanelSources` entry: a function that fills a card, so a new use is a line there. `status` is any part's name and health.
- **The card** (`PanelCard`): up to 6 lines of 32 characters, the first a title in the state's colour; `State` (off, idle, working, ok, wants, refused, fault), which colours the frame so a row of machines reads across a room; a colour swatch with an 8 by 8 picture on it (a material and a section); a bar. It knows nothing of parts, and filling one allocates nothing: text goes in with `Add` from existing strings and numbers.
- **Where its content comes from.** The source reads the part as it is on this peer, so a client's panels need nothing on the wire: they read the same parts and stores the look line does.
- **How it is drawn.** ImGui can't draw in the world pass (it runs over the finished picture, and only between NewFrame and Render), so a panel is plain world geometry: a shared glyph quad path with a signed distance field font (`PanelFontData.cs`, DejaVu Sans Mono Bold, written by `tools/render/make_panel_font.py`; licence in NOTICE.txt): a mipmapped R8 atlas on texture unit 8, sharp at any size, one texture read a pixel, works on GL 4.1; every cell has a clear margin, so a big glyph never picks up its neighbour, one small shader, unlit, depth-tested, every visible panel in one draw at the end of the opaque pass. There is no render target per panel.
- **What it costs.** `Construct.PanelParts` lists a body's parts with panels, so only those are visited, on bodies the frame draws part by part. A panel past its range, facing away or out of view costs a few comparisons and is never filled. One that shows is filled 4 times a second, and its quads are made again only when the card's hash changes. A frame then only turns quads into a stream buffer, so panels follow moving and jointed parts exactly. 120 frames of a full press allocated 0 bytes in the panel path. At most 128 panels show at once.
- **Off a part.** `PanelRender.DrawCard` puts a card on any surface between `Begin` and `Flush`: a held tool's screen does (Tools in hand). A Smart Terminal's screen is the `terminal` source: `PanelCard.AddRow` sets a list's row out with its count at the right edge, and `Mark` lights a line. A card fills 4 times a second, so what a player's own key changes bumps `PanelRender.Touch`, and panels in view fill again in that frame.

## Drones

The user: "a wireless seat connection. I want the player to be able to control drones." A seated player flies or drives a construct with no seat on it. Code: `Source/Constructs/ConstructRemote.cs`, `Source/Game/GameSandboxRemote.cs`; parts and the Scout Drone and Control Post blueprints: `Source/PartKit/Generators/DroneParts.cs`.

- **The receiver is the seat a drone doesn't have.** A **Drone Receiver** has the seat role with nobody in it (`"remote": {"receiver": true}`): link it into a Driving or Turret Module as any seat, and `DriveSeat`, the module's outputs, guns and the drive line read it as they read a seat. F passes over it. So there is no second control path.
- **The sender.** A **Remote Console** is a seat with a transmitter and a screen, 800 m. A **Remote Transmitter** (800 m) or **Medium Remote Transmitter** (3 km), wired from any seat, lets that seat send. Range is the sender's (`"remote": {"range": 800}`), and needs power where the construct has a circuit. The **channel** is a setting, 1 to 99, the same key as the logic parts' Wireless pair.
- **Server authority.** Input goes up the seat path as ever: the server copies the sitter's keys to the receiver every tick (`Game.SandboxRemote`), runs `DriveInput` on the drone and maps the look from the seat's frame to the receiver's, so a Turret Module aims with the mouse. It looks for the nearest receiver at 10 Hz: on the channel, theirs or nobody's, and not driven by someone else. A client knows its drone from who drives what (`ConstructRemote.Piloted`).
- **Lost signal.** Out of range, or with the receiver or transmitter without power, the drone keeps its pilot and gets no keys: its throttle and steering go to nothing and a flyer's hover hold stays on, so it holds where it is and answers again when the signal is back. It carries on what speed it had until drag stops it. When the pilot gets up it has no driver, as when anyone leaves a seat; the hover hold stays on for a flyer.
- **The pilot's view.** Seated with a drone on the link, the player's whole view is the drone's: the frame's own full pass from its first camera (`Program.PilotCamera`), or from behind it with none, starting along the nose. The console's screen keeps the 256x144 feed, for anyone else watching; the pilot's own view is far from it, so it asks for no feed. The mouse looks as far as the camera turns (`PilotYawLimit`, `PilotPitchLimit`: past them the view would look into its own housing). The third-person key orbits the drone, following its heading, and back to the camera; this is its own mode (`ThirdPerson.PilotOn`), so third person left on from driving a car doesn't apply. The body stays in the chair and is drawn there like anyone's. The audio listener, level of detail and render origin follow the view, streamed ground is fetched round the drone on the client and the server (`Game.LocalDrone`, `SandboxInterestAt`), and the HUD says Drone and the signal.
- **A far drone.** The world is whole on every client. What falls off with distance is how often a body's pose goes out, so a pilot's poses are centred on the drone (`SandboxInterestAt`), and what is round it stays current. A piloted body goes in every pose packet already, as a driven one does.
- **A remote turret's sight.** A receiver that aims guns (through a Turret Module) is seen and aimed from its gun sight, not its camera or from behind: 0.3 m past the muzzle of its first working gun and above the bore, raised to 0.7 or 1.2 m when the line along the gun meets the construct's own parts (`ConstructWeapons.RemoteSight`). The server's aim starts there too (`SeatEye` of a receiver), so the crosshair is where the guns converge and the turret's housing is behind the view. In third person the look follows the orbit's crosshair from that sight, as a gunner seat's does (`Program.PilotThirdPersonAim`). The height is chosen on the simulated pose and placed on the drawn one.
- **Scout Drone**: a Drone Frame, four Drone Rotors for lift, two Drone Pushers at the back for W and A/D, a Drone Battery, a receiver, a Driving Module and a camera, 34 kg. Sprint lifts it into the hover hold. Light rotors matter: the hover hold is weak off a planet against a big fan on a small body, and a 750 kg version on Light Lift Fans tumbled under its own pushers.

## Robot shells

Karl's idea (2026-11-08): a body you can risk. Code: `Source/Constructs/ConstructShell.cs` (the part keys, which shell a chair
reaches), `Source/Game/GameRobotShell.cs` (the swap, the link, the wire, the save); parts made by the part kit's `--gen-parts shell_parts`
(`Constructs/Parts/Shells`), recipes in `tools/constructs/assembling.py`. The story's first use of them is the Orbital Depot
(docs/story/DEPOT.md).

- **Robot Shell**: a standing robot, its own construct of one part (`"shell": {"body": true}`), with a channel. A shell fitted
  onto something else can't be woken (stand it on its own).
- **Shell Control Chair**: a seat with `"shell": {"range": 3000}` and a channel. Sit in it for 2 s (`ShellLinkSeconds`) and you
  wake in the nearest Robot Shell on its channel within range that is yours, nobody's or your faction's.
- **The swap.** The player is moved onto the shell and the shell's construct is taken away while they wear it, so a shell is
  played exactly as a player is: movement, prediction, every tool, seats, driving and flying. The body's place in the chair,
  its health, its hands (tool, held part, seat) and its backpack are kept on the server (`ShellRun`); the shell starts with
  full health, empty hands and an empty backpack of the same size. The chair stays taken (its sitter is still them).
- **Leaving.** The Esc menu's "Leave the robot shell" (`CActKind.ShellLeave`), the link dropping (the chair gone or wrecked, or
  the shell more than the chair's range from it, checked every `ShellCheckEvery` ticks), or leaving the game: a Robot Shell
  stands again where they were (frozen, its channel and owner kept) and they are back in the chair, seated. They stand up and
  sit again to link again.
- **Destroyed.** A hit that would kill someone in a shell (`DamagePlayer` asks `ShellTakesDeath` first) destroys the shell: no
  death, no kill line, no respawn, whatever the world's death rules; they wake in the chair with their body's health and things.
  Whatever the shell held or carried in its pack is set down where it was, as a death drops a pack.
- **Seen as a robot.** Every peer knows who wears a shell (`Player.InShell`, `Msg.Shell`, protocol 213) and draws them in
  steel with a lit visor and their colour as the accent (`BodyColors.Robot`, `BodyModel.RobotVariant`). The HUD says so.
- **Saves.** A world saved while someone wears a shell records their body (in the chair) and its things (`ShellCatch`). A shell
  being worn is not a construct, so a game closed with someone in a shell loses that shell (leaving the game first stands it).

Not yet: a body drawn in the chair while you are away; a shell's damage kept between visits.

## Touch panels

With Hands out and empty, the crosshair on a panel is a finger (`PanelTouch.cs`, `Program.SandboxPointer.cs`). A panel declares its buttons in its part file, and any panel can have them:

```json
"picture": [0, 0.44, 1, 1],
"touch": [{ "action": "media.next", "at": [0.68, 0.03, 0.98, 0.24], "label": "Next" }]
```

`at` is x0, y0, x1, y1 as shares of the panel from its bottom left; up to 8 buttons. `picture` is where a page's picture sits (default: all of it). The button aimed at lights up, and LMB fires `action` on the server under the configure tool's rule: contested, permission, reach and power.

To add a button: add the entry above, naming an action. A new action is one line in `PanelTouch.Actions`, a function that changes the part and returns null or why not. A panel source can change a label as it fills its card (`card.TouchLabels[i]`), as the Music Player's Play/Pause does. Buttons don't output a signal yet.

## Video Screens and Music Players

A picture and sound from YouTube links and the game's own tracks, on a part: [MEDIA.md](../MEDIA.md).

## Cameras and screens

The user: "they have an incredibly short render distance for the player. So if there's no players near a functioning screen in the camera system, the camera just doesn't do anything." Code: `Source/Render/CameraFeeds.cs`, `Source/App/Program.CameraFeeds.cs`, `ConstructRemote.FillScreen`.

- **Camera** (and **Medium Camera**): a small part with a lens (`"camera": {"at": [...], "fov": 70}`), looking along its -Z, on any construct. It moves with its segment, so a gimbal is a joint under it. Its channel is a setting.
- **Camera Screen** (0.5 m, 1 m, 4 m): a display panel with the source `camera`, the nearest camera on its channel. It needs power. Its `range` is 8 to 25 m. The Remote Console has one. Any part file can have one.
- **Nothing happens unless someone is watching.** A panel is only filled when it is in range, faces the player and is in view, on that machine ([Display panels](#display-panels)). Filling a camera panel is what asks for the picture (`CameraFeeds.Request`); a camera unasked for 0.6 s stops and gives its layer back. So a camera with no watched screen has no cost beyond one entry in a list, and nothing goes on the wire: each peer draws from its own copy of the world.
- **The limits.** 4 live cameras, in one 256 by 144 texture array. One pass a frame, the camera longest due, 10 times a second at most, and a pass that took long waits 12 times its own time, up to a second.
- **The pass** (`Program.DrawCameraFeed`) runs before the frame's own, into a render target: the map, its sky, baked light and constructs, on the 4.1 and 4.6 paths. It leaves out foliage, decals, glass, panels and weather, and draws players as plain boxes, because their animation is per frame. There are no shadow maps in the game to redo. It allocates nothing.
- **Measured** on the container's software GL at 1280 x 720, in CPU time to submit: 2 to 4 ms a pass on Construct Yard, and 60 to 105 ms on Streamed Terrain, where the frame itself is 260 ms. A real graphics card hasn't been measured.

Choices made (the developer may change them): a screen doesn't check whether a wall hides it from the player; one drone a seat, the nearest on its channel, with no key to cycle them; an owner's drone answers only to that owner's seats, or an unowned one to anyone; the pilot's view always takes over unless third person is on; a seat that drives its own vehicle and sends does both with the same keys; a screen takes the nearest camera on its channel with no range limit; the picture has no static or signal loss.

## Actuator sizes and strength

Board item dev-sandbox-actuator-sizes-as01 (protocol 91). Every moving joint now comes in sizes, and has a **rating**:
the most force or torque it holds rigidly.

### The size families (`Constructs/Parts/Actuators`)

Written by the part kit's `--gen-parts actuator_parts` (`--check` compares; docs/sandbox/PARTKIT.md), as the structural
parts are: three widths (1x1, 2x2 and 3x3 cells of the 0.25 m frame step) times three lengths (short, medium, long), 27
parts in the inventory's Joints and Motion category (sections Pistons, Motor hinges, Turntables).

| Family | Width sets | Length sets | Ratings (1x1 / 2x2 / 3x3) | Length steps |
| --- | --- | --- | --- | --- |
| Piston | push and pull force; side load it takes | stroke (and its top speed, 1 m/s per metre of stroke) | 6,000 / 24,000 / 60,000 N; side 2,000 / 8,000 / 20,000 N | 0.5 / 1 / 2 m |
| Motor hinge | torque | speed (a longer housing holds a bigger motor) | 4,000 / 12,000 / 30,000 N m | 45 / 90 / 180 deg/s |
| Turntable | torque | speed | 3,000 / 12,000 / 36,000 N m | 45 / 90 / 180 deg/s |

Mass, health, armour, idle power and fitting time grow with the size (width squared times length). The three parts that
were there before are one size each and keep their names, shapes, sockets and default settings, so blueprints and
constructs built with them still fit and behave as before: **Piston** is the 2x2 long piston (14,000 N by default, up
to 24,000), **Motor Hinge** the 1x1 medium hinge (3,000 N m, up to 4,000) and **Turntable** the 2x2 short turntable
(6,000 N m, up to 12,000). New sizes start at their full rating and speed. Part ids follow the sorted file names, so
they moved; blueprints and saves name parts, so nothing breaks.

In a part file the `joint` block takes three more keys: `give` (how far it has given at twice its rating: metres for a
piston, degrees otherwise; default a quarter of a piston's stroke, 20 degrees), `sideLoad` (a piston: N across its axis
before it gives; 0 no limit) and `overloadDamage` (hp a second at twice the rating while overloaded; 0, off, on every
shipped part).

### The configure tool

Strength (Force or Torque) and Speed run from a tenth of the size's maximum up to it, in the part's `config` schema, so
they clamp to the size's range like every setting; a lower strength caps the power it can draw (the drain is the
motor's push times its speed), and a lower speed draws less while it moves. The panel shows a **Load** bar: what the joint holds against its rating ("Load 4200 /
6000 N (70%)"), green up to the rating and red past it ("overloaded, giving way"). Overloaded actuators are tinted red
in the world, deeper the further past their rating (full at twice), pulsing a little. The load comes from the server in
the pose (a byte a joint, hundredths of the rating).

### The strength model (`ConstructPhysics.MotorPrep`)

- A moving joint's motor follows a **reference**: the target its input asks for (a turntable: its speed, or where it
  stopped when its input is 0), reached at the joint's speed.
- The motor holds the joint on the reference with at most its rating: a velocity constraint solved with the contacts
  and the other joints (the joints and the contacts on jointed constructs get 6 more solver passes than the rest, so a
  chain of actuators converges). **Up to its rating the joint is rigid**: no spring, no sag, no jitter.
- A load past the rating pushes it off the reference, and the further off it is the harder the motor pushes: rating x
  (1 + sqrt(behind / give)). It settles where that meets the load, **give x (load / rating - 1)^2** behind: a little
  just over the rating, more and more as the overload grows. Moving off the reference is damped (no bounce).
- It never asks to move faster than its speed, so when the load goes it comes back **at its speed**.
- At 4 times its rating it slips (the reference is dragged along): a hugely overloaded lift sinks to its stop.
- A piston loaded across its axis past its side load holds that much less along it (rating / (side load / its side
  rating)).
- The load counts everything: the weight it carries (riders too), the contacts on the parts beyond it (feet on the
  ground, a post in the way), inertia and other joints. A motor driving into an obstacle shows as overloaded and pushes
  at it a little past its rating (more the further its reference has run ahead, at most 4 times).
- A gunner's or sentry's aim and steering pivots keep the plain motor (at most their strength).
- An overloaded joint stays awake (it doesn't sleep giving way); one at rest within its rating sleeps as before.

### Caps and cost

A construct may have 16 joints (was 8), so a 10-joint leg or arm fits; still 128 in all. A 10-joint arm costs about
0.06 ms a tick on the cloud box; no allocation per tick.

### Self-test

"Construct actuators" (`SelfTestConstructActuators.cs`): the three families' nine sizes each (width raises the rating,
length the stroke or speed, size the mass, health and power; overload damage off); a construct on four piston legs, and
one on two-joint legs (motor hinge knees), each rated a quarter over its load and kept awake for 5 s: no sag or tilt
over 0.2 mm (it measures thousandths), then it sleeps still; a lift loaded to 1.25, 1.5 and 2 times its rating gives way
along the curve without bouncing and comes back at its speed, a client hears the load and the panel's text says so,
overload damage takes nothing while off and wears the part when on; the size in play (stroke, force, speed); the
configure values clamp to the size's range and change what the piston holds and how fast it goes; a tower of six
pistons and a ten-hinge arm worked by seeded inputs and overloaded stay finite and in one piece, the bent arm holds its
tip still, ten joints travel to a client, no allocation, and the arm's tick cost (budget 1 ms).

### Decisions made (the developer may change them)

- **Overload damage is off** (the developer's open question): `overloadDamage` is 0 on every part; set it (hp a second
  at twice the rating) to make sustained overload wear a part.
- The yield curve (give x (overload - 1)^2), the give (a quarter of a piston's stroke, 20 degrees for hinges and
  turntables) and the slip at 4 times the rating.
- The ratings, side loads, strokes and speeds in the table; a longer motor hinge or turntable is faster (not slower and
  stronger, as a gearbox would be), since the width already sets the torque.
- New sizes are plain boxes centred on their origin (not offset onto the 0.25 m lattice for even widths); the old three
  keep their shapes.
- Steering pivots have no sizes (they are mechanical, not motors).

## Linear joints

A rail and a carriage: what hangs on the carriage slides along the rail. Data: `Constructs/Parts/Linear` (written by
`Source/PartKit/Generators/LinearParts.cs`, 42 parts; `--check` compares). Code: `Source/Constructs/ConstructLinear.cs`. Inventory: Joints and
Motion, section Linear; size is the window's filter. Existing Pistons stay as they are.

| Kind | Joint | Runs on | Input | Unwired |
| --- | --- | --- | --- | --- |
| **Free Slider** | `slider`: no motor, rolls under load between its stops | Nothing | `Lock`: on, it holds | Rolls; F says it has no motor |
| **Linear Actuator** | `piston` motor and [strength model](#the-strength-model-constructphysicsmotorprep), draws power by load | The circuit (`Power in`) | `Set`: 0..1 along the rail (Position), or -1..1 of its speed (Speed); `Hold`: on, it stops where it is | Sits at its Rest position; F sends it to the far end and back |
| **Shaft Linear Drive** | The same motor, through a lead screw | A drive shaft into `Drive in` | `Set`: -1..1 clutch and direction, 0 holds | Runs while the shaft turns; F reverses it |

Sizes (travel is the section's length; the rating is the Force setting's top, a tenth of it the bottom):

| Class | Gauge | Travels (m) | Rated force | Top speed | Lead (m a turn) |
| --- | --- | --- | --- | --- | --- |
| Small | 1x1 | 0.5, 1, 2 | 6 kN | 2 m/s | 0.05 |
| Small | 2x2 | 1, 2, 4 | 24 kN | 2 m/s | 0.05 |
| Small | 3x3 | 2, 4 | 60 kN | 2 m/s | 0.05 |
| Medium | 1 m | 4, 8 | 300 kN | 2 m/s | 0.2 |
| Large | 4 m | 16, 32 | 5 MN | 3 m/s | 0.8 |
| Capital | 16 m | 32, 64 | 200 MN | 4 m/s | 3.2 |

A driven rail takes 5 times its rating across its axis (a piston a third of it): it is made to carry what rides on it, so a
flat rail holds the load off the axis. A Free Slider has no rating: it holds any load, across or at its stops.

### The joint

A section's axis is its length, and the carriage rests at the section's middle, on the leaf socket `top`. The joint's number is
the carriage's distance from there, so unlike a piston's it runs below 0 as well as above. Hanging parts on the carriage works
as on any joint (`ConstructBuild.OnLeaf`, `LeafRefusal`, `HangOnLeaf`; attach tool, socket attach and freeform attachment alike). The carriage
is drawn, not solid: what hangs on it collides. Riders and vehicles ride what is on it (`ConstructDeck`, contraption space), and
the Load bar counts them.

### Stacking

Sections joined end to end by their `end-` and `end+` sockets make one rail: three 2 m sections are 6 m of travel, with one
carriage and one joint, and nothing chained. `Construct.BuildRails` works it out on every rebuild, on every peer, before anything reads
a joint's limits; none of it is saved or sent (the wire and the save carry ordinary parts, links and the joint's number):

- **What joins.** Same gauge and size class, on the same moving piece, in line within 2 cm and 3 degrees, either way round; travels may
  differ. A freeform join's links count. At most 64 sections (`MaxRailSections`).
- **One carriage.** The joint is the section whose carriage carries something (`CJoint.Hinge`); the run's limits go on it, in its own
  axis, so a load on a middle section runs both ways. Other sections hide their carriages, and a second load on the same rail is
  refused ("it has one carriage"). Two rails already carrying loads, pushed together, stay two rails, split at the second one.
- **Mixed stacks.** A powered section anywhere in a run drives the lot: `CJoint.Drive` is that section, and its wires, power, shaft and
  settings are the joint's, whichever section's carriage carries the load. Free Slider rails are the cheap way to lengthen an actuator.
  The first powered section along the run wins; a Lock wired to a Free Slider in a powered run does nothing.
- **Settings.** The configure tool, on any section of a run, opens the section that runs it (`ConstructLinear.Master`).
- **Taking it apart.** A section wrecked or taken off shortens the rail, and a wrecked one splits it; each piece is a rail of its own. A
  carriage left out past the new end runs back at 2 m/s rather than jumping. The look line says "a wrecked section cuts this rail short".
- **Cost.** A run is one joint out of `MaxJoints`, one slot in the solve, and nothing per tick beyond that; the walk runs only on a rebuild
  of a construct that has rail parts.

### Motion

- **End stops** are the joint's limits, solved as rigid rows with the contacts. `stopLow` and `stopHigh` (configure tool, % along the whole
  rail, a stack too) move them in.
- **Free Slider**: gravity along the rail, what pushes on the parts beyond it, and `SliderDrag` (0.8 a second). Like every joint it is reduced
  coordinates: it feels gravity and contacts, not its base's acceleration, so a carriage doesn't run back as a car brakes.
- **Linear Actuator**: `rest` (where it sits unwired, 50% by default, so a single section doesn't move when powered), `setMode`, `speed`, `strength`.
  In Speed mode a Set of 0 holds; at a stop it stops pushing. Power is the motor's force times speed, like a piston's.
- **Shaft Linear Drive**: the speed is shaft rpm / 60 x lead (`lead`, in the configure tool), capped at the part's top speed. The force is the
  lower of its rating and 2 pi x engine torque x gain / lead, gain the line's reduction and losses and the screw's 90%: a shorter lead pushes harder and goes slower, as
  a lower gear does. It uses the drills' feed (`ConstructDrillDrive.BuildShaftFeeds`, `Linked`): a drive shaft to `Drive in`, or a
  drive part on that socket, through a gearbox, a clutch or a power take-off, or straight from the engine. Moving, it takes its force back from the engine
  (`Construct.RailLoad`, N m at the crank): less for the wheels, more fuel, and an engine that can't carry it is pulled down and it slows (`ShaftShare`).
  Reverse gear or a reverse throttle reverses it. A lead screw doesn't back-drive: with the engine off, the shaft cut or the clutch out it **holds**, up to its rating, and the look
  line says why. Its **Back-drive** setting (configure tool, off by default) gives it a steep screw instead: whenever the shaft isn't turning it, or `Set` is 0, it lets go like a
  rail with no power (`ConstructLinear.LetsGo`), with a little drag, so a load pushes the carriage back and the look line says "free" rather than "held". A running shaft still drives it at its rating.

### Network and saves

Protocol 135: joint type byte 7 (`Slider`), and a sliding joint that reaches past 30 m sends its pose in centimetres, not millimetres (a pose is
a short: millimetres stop at 32.7 m, which also held a 64 m Capital Piston short on clients). World saves store the joint and its limits as before.

### Decisions made (the developer may change them)

- A section's travel is its whole length, so the carriage overhangs a rail's end by half its length at the stops.
- Stacking doesn't add force: a run has one motor. Parallel rails on one load do not share it either (no closed loops).
- The numbers in the table, the side load (5 times the rating), `SliderDrag`, the screw's 90%, and the lead's range (a fifth to 4 times the default).
- A shaft rail is a self-locking screw unless its Back-drive setting is on. Back-driven, it has only the drag of unpowered gears and no static hold, and the physics gun still can't move it.
- The Set input of a Shaft Linear Drive is a clutch and direction; position by shaft isn't offered.
- Lock on a Free Slider is on or off, not a graded brake.

## Pieces of a mechanism

A construct with joints is a set of **pieces**: the base, and each segment a joint moves. Code: `Source/Constructs/ConstructFreeChain.cs`, `ConstructEdits.FreezePiece`. Protocol 139.

**Freezing a piece** (the physics gun):

| Press | What it does |
| --- | --- |
| Aim (RMB), holding a whole construct | Freezes every piece of it |
| Aim, holding one piece | Freezes that piece and lets go |
| Aim at a piece, holding nothing | Freezes or unfreezes that piece alone. The look line says which |
| Fire on a frozen piece | Unfreezes that piece alone and takes it |
| Fire on the rail or base of a frozen construct | Unfreezes all of it and takes it, as before |
| Reload twice | Unfreezes everything you froze |

So the way to debug a contraption: hold it and freeze it, then unfreeze the pieces you want moving, one at a time. Outlines and the frost tint show frozen pieces only.

- **Frozen means it stays where it is in the world**, whatever the others do. On something fixed (a frozen piece, a frozen or anchored base) the joint is locked (`CJoint.FrozenLock`): it costs no solve. On something free it is held by rows at three points (`ConstructPhysics.PinRows`, nine rows a piece), so with the base unfrozen and a pendulum frozen, the base swings and slides round the pendulum.
- A motor on a frozen piece lets go and picks up from where it stands when unfrozen. A flexible joint's ball freezes as one.
- One owner a construct: `Construct.FrozenBy` is who last froze a piece of it, and that is whose double Reload frees it.
- **Saves**: a joint flag; the pin is taken again from the saved pose. **Joiners**: a full body's joint flags. **Blueprints**: `"frozenBase": true` and `"frozenJoints": [steps]`.
- Frozen constructs saved before this keep their joints free: only the base is frozen. So does a survival rig. Those joints freeze with Aim like any others.

**Joints pass motion on** (`ConstructPhysics.CoupleJoints`). Each joint is one number, solved alone, so a slider's stop used to mean nothing to the pendulum on it. Now each pair in a chain with a free joint in it shares momentum, from the mass they share: the mass matrix's off-diagonal term at the centre of what the outer joint carries. What changes of that each tick goes to the free joints of the pair, before the solve, 85% of it (`CoupleShare`: one number solved a tick behind the other rings otherwise). A slider that hits its stop kicks the pendulum out; a swinging pendulum works the slider. Free joints take it, motors don't: a motor's arm stays rigid, though it swings a free joint on it. Pairs are found on rebuild (at most 128 a construct), and a construct without a free joint in a chain has none. A free body's own acceleration isn't passed to its joints.

## Free rotation

Joints that turn with no motor, spring or power. Data: `Constructs/Parts/Free` (written by the part kit, `--gen-parts free_parts`, 12 parts; `--check` compares: docs/sandbox/PARTKIT.md). Code: `Source/Constructs/ConstructLinear.cs` (`JointType.Swivel`). Inventory: Joints and Motion, section Free rotation.

What was there before: a **Hinge** swings, but it springs to its latch and its leaf takes doors; **turntables, motor hinges and pistons** have motors and hold still without a signal; **flexible joints** spring back. Nothing turned freely.

| Part | Turns | Sizes |
| --- | --- | --- |
| **Free Swivel** | About the line through its leaf socket: a lazy Susan, a free turret ring | 1x1, 2x2, 3x3, Medium, Large, Capital |
| **Free Pivot** | About an axis across its leaf socket: a pendulum, a swinging arm | The same |

- Gravity on what hangs on it, what pushes on the parts beyond it, and a little drag (`ConstructLinear.SliderDrag`, 0.8 a second, as a Free Slider). It feels gravity and contacts, not its base's acceleration.
- A `Lock` signal holds it. **Limits** are a setting, off to begin with: `limited`, `min`, `max` (degrees). Off, it turns forever.
- The moving side (`"side": "leaf"` boxes) turns with what hangs on it, and the physics gun turns it by that side ([The physics gun](overview-and-playing.md#the-physics-gun)).
- A Free Pivot with its leaf up falls over until something stops it: hang it leaf down for a pendulum, or set limits.
- Protocol 137: joint type byte 8.

Decisions made (the developer may change them): the names; the same drag as a Free Slider, so a spun Free Swivel stops in a few seconds; no rating (it holds any load, across or along its axis).

## Flexible joints

The user's request: "there needs to be a flexible joint connection for items", which turned out to mean both a joint part
with free play (here) and a chute that survives one ([Flexible chutes](#flexible-chutes)). Code: `Source/Constructs/ConstructFlex.cs`.
Data: `Constructs/Parts/Actuators/FlexJoints` (written by the part kit's `--gen-parts actuator_parts`, which draws their sides too).

A small passive part. What hangs on its leaf bends any way up to a cone and springs back; there is no motor and it draws no
power. Six parts in the inventory's Joints and Motion category, section Flexible joints:

| Part | Bends | Twists | Stiffness (1x1 / 2x2 / 3x3, N m a degree) | Rating (N m) | Joints it takes |
| --- | --- | --- | --- | --- | --- |
| Flex Joint 1x1, 2x2, 3x3 (rubber) | 25 deg | no | 80 / 320 / 960 | 2,000 / 8,000 / 24,000 | 2 |
| Ball Joint 1x1, 2x2, 3x3 | 50 deg | 30 deg each way | 16 / 64 / 192 | 2,000 / 8,000 / 24,000 | 3 |

In a part file the `joint` block has `"type": "flex"`, `axis` (out towards the leaf: what it twists about), `pivot`, `leaf`,
`cone` (degrees, 2 to 80), `twist` (degrees, 0 for none), `stiffness` (N m a degree), `damping` (a ratio, default 0.7),
`strength` (the rating) and `overloadDamage`. The configure tool sets the Bend limit (up to the part's cone), Stiffness and
Damping; the Load bar shows what it holds against its rating.

**It is not a third kind of joint.** A flexible joint is two or three ordinary one-number joints in a row at one pivot, all
naming the same part (`JointType.Flex`): a swing about each axis square to the leaf, then a twist about the leaf. The
segments between them carry no parts, and the leaf's parts sit on the last (`Construct.LeafSegOf`; `JointOf` finds the first,
so `JointSides` and anything counting "the parts under it" just work). So every system that walks a joint chain needed
nothing new: `SegPose`, `MoveSeg`, the contact and coupling rows, recoil, `SegPointVel` (riders), drawing, the pose on the
wire (angle, speed and load per joint), blueprints (a step on the leaf makes the joints), splits and joins. The pieces can
never drift apart, as for any joint. What it costs is slots: **a rubber joint takes 2 of a construct's 255 joints and a ball
joint 3**, and the refusal says so (`ConstructPartDef.JointSlots`, `ConstructBuild.LeafRefusal`). The gimbal's flip at 90
degrees is out of reach, since a cone is at most 80.

**Physics** (`ConstructPhysics.FlexPrep`, a row in `JointRows`). The spring is a soft constraint solved with the contacts,
implicit, so no stiffness can blow it up. The damping is a ratio of what critically damps this tick's load, so it settles
alike under a rock or a player. It settles at load / stiffness. The cone is a circle, not a square: each swing may reach what
the other leaves (`FlexReach`), and what passes the circle is put back on it (`FlexAfter`). The stops are the ordinary limit
rows: **rigid**, so a load past its rating sits on the stop and shows overloaded (`Over`, the red tint), with
`overloadDamage` (off) to make that wear it. Its reaction goes to the body like a motor's.

- A wired signal locks it where it is, as for a passive hinge. F says what it does and refuses (it has no latch).
- Drive shafts can't cross it (`"a drive shaft can't cross a joint"`), and a U joint against it says so. A chute can.
- Riders on the leaf count towards its load (their weight, at the carried mass's centre, not their feet).
- **Wheels beyond it don't roll.** A wheel is a raycast wheel only on the body or a steering pivot on the body
  (`Construct.IsRaycastWheel`): beyond a flexible joint, as beyond a hinge, it is a part that touches the ground. Its force
  would have to go through the joint chain; that is future work with the collision rework.
- Not modelled, as for every joint: the body's acceleration doesn't swing what hangs on the joint (a flexible arm on a
  braking truck doesn't lurch), only gravity, contacts, couplings and recoil do. The leaf's half of the part is drawn
  bending, but the part collides as one box on its base side.

Decisions the developer may change: the sizes and numbers above; `stiffness` x `cone` = rating on a rubber joint (it reaches its
stop at its rating), and 0.4 of that on a ball joint; a joint's budget (raise `Construct.MaxJoints` if players want long
flexible chains: every array scales with it, but each construct pays for it); overload stays off, as on the actuators.

### Flexible chutes

What happened before: a chute could already be routed across a joint (only shafts are refused), checked in the rest pose only.
Moving, it was drawn as one straight box between its two ends, however far they went (through the parts, stretched without
limit, right round a turntable), and went on carrying, with no limit and no reason. The bend rule and the clipping rule were
never looked at again. Now (`Source/Constructs/ConstructFlexChute.cs`):

- A chute segment whose two ends are on different moving segments (`Construct.MoveSeg`, so a freeform attachment doesn't count) is a
  **flexible section** by itself: no new part, nothing to pick. It is drawn as a ribbed bellows curving between its ends
  (`RouteRender.Bellows`), and is free of the duct's 45 degree steps and of clipping, since where it runs at rest means
  little. It carries bulk and parts as any chute does.
- **Its limit**: it stretches to 3 times its rest length (at least 0.5 m more), folds to 0.3 of it, and its ends turn 135
  degrees apart. The bellows goes from the duct's colour to red over the last 30%, and the look line reads "flexible section
  1.4 of 3.0 m, bent 40 of 135 deg". **Past it the chute stops** ("STOPPED, stretched too far (bring the joint back)") and
  carries on when the joint comes back. **Past 1.25 times its limit it tears**: the segment is cut as if shot, a hint says
  so, and the attach tool mends it. A branch stops with the link it comes off.
- So a long-stroke piston needs a long rest section (a node on each side of the joint sets it), and a chute over a free
  turntable stops at 135 degrees and tears at 169: use a chain of them, or put the store on the axis.
- Nothing new on the wire: both peers work a section's strain out from the joints, and a tear is the link's health.
- Only chutes flex like this; cables and pipes across a joint are drawn straight as before, with no limit.

**Between constructs: the cargo hose.** Hoses carried no items, and a world link snaps when a free construct moves. Two new
hose parts (`Constructs/Parts/Hoses`, `"carries": "items"`): the **Cargo Hose Reel** (15 m, a load a second, chute input "Cargo
in") and the **Cargo Hose Inlet** (chute output "Cargo out"). Run a chute from a store into the reel on one construct, and from the
inlet to a store on the other; F takes the coupling and F plugs it in, as for any hose. What moves goes straight from the
store feeding the reel to the store the inlet feeds (`ConstructRoutes.HoseMove`): bulk or parts, chute rules as ever, and past 15 m
it pops out unhurt and reels back, so a docked vehicle can drive or fly off. It is a person's job, one way (reel to inlet;
fit a pair for both ways) and stops at no fixed dock: **no automatic coupling yet** (a docking port that plugs itself would be the next
step for the mothership loop). A plugged cargo hose joins the two sides' beacon networks.

## Control modules

A seat controls nothing by itself: a car with a seat and no module doesn't go, and a gunner's seat doesn't work the gun
beside it. A seat has one output. One data link from it into a **module** carries the sitter's controls, and the module
breaks them out. Old saves and player blueprints have no modules: they stop driving until they are rewired. The shipped
blueprints have them. Code: `Source/Constructs/ConstructModules.cs`; parts: `Constructs/Parts/Controls` (written by
`tools/constructs/control_parts.py`, in Seats and Logic > Controls).

**Driving Module.** Link the seat into it, and its `drive` output to any one part of the drivetrain: an engine, a
gearbox, a steering pivot, a thruster or a hub motor (each has a `drive` port, which the picker offers alone for this
link and hides for every other). Those two links are all a car needs: that seat is now `Construct.DriveSeat` and everything
it did before runs for the whole construct: throttle, steering, brakes, the starter, shift keys, hover and lift, skid
steering, a steering differential, the jump drive. The drive link carries no signal into the part it ends at: a wired
gear signal or throttle on the same part works as ever.

Its other outputs are the same controls one by one, for a builder who wires by hand instead:

| Output | Value |
| --- | --- |
| `drive` | 1 while someone sits; as a link, the whole drive |
| `steer` | -1..1, left is plus, eased, less at speed (straight into a steering pivot) |
| `throttle` | -1..1; 0 while Back brakes a car rolling forward |
| `brake` | 1 on Jump, or Back while rolling forward (a Brake part) |
| `reverse` | 1 while it goes backwards |
| `handbrake` | 1 on Jump only |
| `run` | 1 while someone sits (an engine's run port) |
| `gear up`, `gear down` | 1 while Reload, Sprint is held (a Gear Selector's input) |
| `roll` | -1 to 1: the seat's roll keys, Q and E (left, right; rebindable, and only in a seat). Unwired thrusters take it too, by how they sit about the seat's forward axis; holding a height, it leans the lifters |
| `lift` | -1..1: Sprint, Crouch |
| `left`, `right` | Each side's drive for skid steering, -1..1 (a hub motor's input) |

A construct wired only by hand has no `DriveSeat`: its engine runs on its wires, as a seatless one does. Forward still
comes from the module's seat (`Construct.ControlSeat`), or the first seat on the body.
A tank wired by hand steers with `left` and `right` into hub motors, or a signal into a steering differential's `Steer` input. Thrusters' yaw mix likewise comes from the `drive` link; by hand each
thruster takes its own signal.

**Turret Module.** Link the seat into it, and its `guns` output into each gun of the turret. A **turret** is a gun and
the turntables and motor hinges under it (`CGun.AimJoints`); several guns on one module all follow the same look. The
sitter's look points those joints, overriding wires on them, and Fire fires the guns. A gun with no joints under it just
fires. A seat need not ride the turret: one on the hull works a turret on the roof, and looks all the way round.

Every seat links to its own module, so one construct holds any number of turrets: gunner seat 1 on turret A, gunner seat
2 on turret B, a driver driving. A seat linked into several Turret Modules works one at a time: **Aim** steps to the next
(the seat's `State`), so one seat can have several turrets and a driver can have a hood gun.
A Turret Module's outputs: `guns`; `fire`; `yaw`, -1..1, half a turn either way from the seat's front, right is plus;
`pitch`, -1..1, straight down to straight up; `aim`; `reload`. `yaw` into a turntable the seat stands on turns it towards
the look and stops; `pitch` into a motor hinge sets its angle. Wire these with the `guns` output off if you want your own
control. `guns` reads -0.2 while the module is the seat's turret (no trigger; it tells clients which one is in use) and 1
while firing.

**Saying why.** The look line and the seated HUD say why a seat does nothing, in the usual format: `Seat: free - no
Driving Module linked: this seat controls nothing - F: sit`; a module says `no seat linked into it` or `drive output not
linked`. A passenger's seat on a driven vehicle has no fault. `ConstructModules.Problem` holds all of it.

**Network and saves.** Nothing new travels: the links are ordinary data links in the routes every peer has, so each peer
works out `DriveSeat`, `ControlSeat` and `TurretMods` in `Rebuild`, and blueprints, saves, joiners and selections carry them
as they carry any link. The protocol went to 139 because older peers would disagree on who drives. Modules are not
electrical: a flat battery stops an electric motor, not the seat's controls. Output ports past the first two aren't
stored: they are worked out from the seat's keys when read (`ConstructModules.Output`), so nothing allocates and a module
costs nothing while nobody reads it. A capital part's data port is on its top face, so a link reaches it.

### Choices made (the developer may change them)

1. **One link to the whole drivetrain.** The `drive` output hands the construct over as a seat did, rather than feeding six wires; single outputs are there for those who want them.
2. **A Traction City's bridge moved** to the deck's front corner, because a link reaches 24 m and that is as far as a drive part is from a 128 m hull's edge.
3. **Modules need no power** and are a tier 1 Assembler recipe (steel and rubber, no circuit board), so a first car doesn't wait for electronics.
4. **Aim steps turrets** because Fire is the gun and R is the gearbox on a seat that drives too; the `aim` output still reads it.
5. **One size.** A capital hull takes small parts through a size adapter, as it takes a seat.
