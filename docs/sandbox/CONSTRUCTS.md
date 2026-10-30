# Constructs and the Sandbox mode: milestones 1 and 2

The sandbox survival mode (board item dev-idea-constructs-cs01; the full design and the developer's 16 decisions are in
the project doc `claude/sandbox-construct-mode.md`) is being built in milestones. **Milestone 1 is the construct core,
proven on one small test map**: parts, tools in hand, ghost sockets, one rigid body per welded group, a working
drivetrain, a passive hinge, per-part damage with wrecks, and all of it server-authoritative over the network. There is no
large world, streaming, factions, enemies, points of interest, resources, scanning, terrain digging or construct weapons
yet. **Milestone 2 adds joints, logic and the drivetrain as parts** (decisions 3 and 4): motorised hinges, turntables,
pistons and steering pivots; logic parts wired with a linking tool; transmissions and differentials you can shoot;
passengers, riding on moving decks and run-over damage; and the yard's lift, turret, motorised gate and wired buggy to
test them on. Everything new is listed under [Milestone 2](#milestone-2-joints-logic-and-the-drivetrain).

## Playing it

Host a game on **Construct Yard** (Dev pack) and pick **Sandbox** (a Dev-only mode, like the template). The yard has a
buggy, a wired buggy (a passenger seat, a horn on the driver's fire key, headlamps on aim), an anchored gate, a gate on a
motorised hinge with a button, a lift, a turret on a turntable with a lever, a parts rack (the yellow shelf in the
north-west corner) and some loose parts.

| Key | What it does in Sandbox |
| --- | --- |
| 3 / 4 / 5 | Take out the **wrench** / the **welder** / the **linking tool** (press again to put it away). 1 or 2 go back to your guns. (4 and 5 are the kill-streak keys elsewhere; Sandbox has no streaks.) |
| F | At the rack: take a part (press again for the next kind). On a seat: sit (the first seat drives, any other rides). On a door, hinge, motorised hinge or piston: open or shut it (unless its wires run it, or it's locked). On a button, switch or lever: work it. On a loose part: pick it up. In a seat: get out. |
| Fire (hold) | With a tool out and a part in hand: fit the part on the ghost socket under the crosshair (or set it down on the ground as a new construct). Letting go early stops it. |
| R | Turn the part on its socket (the next way round that fits). |
| Aim (hold) | With a tool out: take the part under the crosshair off (into your hand, or dropped if your hands are full). |
| T | Drop the part in your hand. |
| I | The debug **parts menu** (below): any part into your hands, any blueprint in front of you. I or Esc closes it. |
| Middle mouse (hold) | The **tool wheel** (below): flick to a tool, let go to take it out; let go in the middle to cancel. |
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |
| Fire / Aim / R (seated) | Held keys the construct's **input readers** can read (drivers and passengers don't shoot). |
| Linking tool | Fire on an output, then fire on an input: a wire. Aim on a part: cut its wires (or let go of a wire in progress). R on a logic part: its next setting. |

### The tool wheel (decision 17)

The developer's decision 17 (2026-10-29): players pick their tools from a radial menu. Hold **Tool wheel**
(`GameAction.ToolWheel`, the **middle mouse button** by default, rebindable; added by binds version 10. Q was taken by
Swap weapon, and the middle button was free everywhere) and eight segments appear around the middle of the screen,
clockwise from the top: **wrench** (N), hoist (NE), **welder** (E), configure tool (SE), **hands / gun** (S: out of
build mode), scanner (SW), **linking tool** (W), hand drill (NW). The four tools that exist sit on the compass points, so
the easiest flicks reach them; the four from the design's tool list that don't exist yet sit on the diagonals, greyed out
with "coming later" (letting go on one says so and changes nothing). While the key is held the view stands still and the
mouse moves the wheel's pointer instead (accumulated, held within 70 px of the middle, so a small flick turns it to
another segment); the segment is picked by direction (a 22 px dead zone in the middle). The highlighted segment gets an
orange arc and its name and favourite key show in the middle ("key 5: wires, settings"); the tool in your hands is marked
with a green inner arc and dot. Letting go takes the tool out through the very request the number keys send
(`CActKind.Tool`, server-authoritative), so nothing on the wire changed (protocol 79) and the number keys stay as the
favourites. Letting go in the middle cancels. You keep walking while it's open; the mouse buttons don't reach the gun or
the tools meanwhile (so opening it lets go of a fit in progress). It doesn't open in a seat ("get out of the seat
first"), and it closes without choosing on death, any menu (the parts menu included: only one is open at a time), the
chat, or the match ending; a key still held after that doesn't reopen it.

The widget is generic (`Source/UI/RadialMenu.cs`): items with a label, a hint, a note, an enabled flag and an icon id; the
owner opens it, feeds it mouse movement and asks which segment was chosen on release; it draws with ImGui's draw list
(annular sectors, arcs, the icons through a delegate) and allocates nothing per frame. It is meant to be reused for parts
to place, logic components, pings or emotes. The Sandbox wheel is `Source/UI/SandboxToolWheel.cs` (its icons are simple
shapes from lines and boxes: an open-jaw wrench, a torch with sparks, two terminals and a wire, a box pistol, a hook on a
chain, three sliders, a handset with waves, a drill); `Source/App/Program.SandboxWheel.cs` hooks it into the frame.

Choices made for the wheel (the developer may change them): the middle mouse button, not Q (Q swaps weapons); the wheel
sits at the screen centre (the cursor is captured, so "around the cursor" is the centre); the tools still to come are
shown greyed rather than left off (eight segments read cleanly as a compass, and they show what is planned); it is
closed in a seat (seated keys belong to the construct's input readers); no tool modes ring or second page yet (no tool
has modes); no stick support yet (no Steam Input in the game).

### The debug parts menu

A debugging aid for the test yard (the developer asked for one, 2026-10-29), not how the final game will hand parts out:
**I** (the inventory key, `GameAction.Inventory`, which Zombies uses for its parts inventory; rebindable) opens **Parts
(debug)** (`Source/UI/UiSandboxParts.cs`), the mouse free for it. It lists every loaded part grouped by role (frames,
panels, seats, wheels, ..., logic), with a search box (name, role or description), and for each its colour swatch (its
first and last boxes' colours), the tool it needs, mass, health, armour, fitting time and description. Click a part, or
move the highlight with Up/Down and press Enter: the client asks the server for it by id (the rack's `CActKind.Rack`
request with a part id, so the server checks it: an unknown id is refused) and takes out the tool it needs, so the
ghost sockets, fitting and dropping work exactly as with a part from the rack. A **recent** row keeps the last six picks.
The **blueprint buttons** (one per blueprint file) ask the server to build that blueprint standing on the ground in front
of you, facing the way you look, far enough out not to land on you (`CActKind.Blueprint`; refused outside Sandbox). The
window closes after a pick unless "Keep open after picking" is ticked; I again or Esc closes it. It closes itself when you
die, sit down or the match ends, and it isn't offered in a seat. The rack works as before.

It is only in Sandbox, and a map can turn it off with `"partsMenu": false` in its `"sandbox"` section (a future
survival map will): the key then says "the parts menu is off on this map" and the server refuses both requests. The
window allocates nothing per frame while open (every row's text is made once).

Editing always needs the right tool in hand (decision 2): **welder** for frames, panels, corners, decks and foundations,
**wrench** for everything bolted (seats, wheels, suspensions, engines, tanks, fuel lines, transmissions, differentials,
hinges and joints, doors, logic parts), the **linking tool** for wires and logic settings. With a gun out
or bare hands you can only use things: sit, drive, open doors, pick up and drop loose parts. Fitting takes the part's
`attachSeconds` (welded parts 2.5 s, bolted 1 to 3 s), taking off 60% of that. The HUD panel shows what is in your hands,
what you look at, the edit's progress, refusals and, when driving, speed, fuel and why it won't go.

## What a construct is

- A **construct** is a set of parts joined by **links** (a weld or a bolt between two sockets). Everything joined
  directly is **one rigid body**; parts that hang on a **hinge** are in a **segment** that turns about the hinge (a
  joint is an angle on the body, so it never drifts apart).
- The **root** is the first seat, else the first foundation, else the first part. After any part goes, everything no
  longer joined to the root splits off as a **wreck** (its own body, moving as that piece moved). The broken part itself
  falls away as wrecked debris. A wreck has no root, can be edited like anything else, and can't be picked up whole.
- **Anchored**: a working foundation holds the construct still (a base, a gate). Free constructs move by physics.
- **Mass and centre of mass** come from the parts; inertia from their boxes.
- Parts sit on a 1 cm grid and turn in 90 degree steps (24 turns), so every part box is axis-aligned in its body and
  overlap checks are plain box tests.
- **Ghost sockets**: holding a part with a tool out shows a marker on every free socket of the construct you look at
  that the part fits (matching socket kinds, facing each other, nothing in the way); the one nearest the crosshair shows
  the whole part, green when it fits.

### The drivetrain (it only works when connected)

A construct drives when it has a working **seat** with a driver (or a signal wired into its engine), a working **engine**
in the body, **fuel** reaching the engine (a working tank linked to the engine directly, or through a chain of working
**fuel lines**), a working **transmission** touching the engine, and **wheels on working suspensions** bolted to a working
**differential** the transmission feeds (touching it, or through other differentials), directly or through a steering
pivot. Only those wheels are driven; any other wheel rolls (and steers, and brakes). A missing or broken link stops it, and
the HUD says which: "no fuel line from the tank to the engine", "the transmission is wrecked", "no wheels on the
differential"... Transmissions and differentials stop working at zero health (`disable`) rather than falling off.

### Physics

`Source/Constructs/ConstructPhysics.cs`, server only, at the fixed 60 Hz tick:

- Rigid bodies with box inertia, integrated with gravity (the game's own gravity and the host's gravity setting).
- Against the map: up to 40 **hull points** per body (part corners, the outermost kept), each probed with a ray down and
  a ray along its motion, giving speculative contacts solved with sequential impulses and friction.
- Between constructs: hull points of one against the part boxes of the other.
- **Raycast wheels**: a spring-damper along each suspension (stiffness from the body's mass and the suspension's
  frequency), side grip, drive force shared by the touching wheels, brakes, a friction circle, and tyre forces applied
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over.
- **Joints** (reduced coordinates: each is one number on its body): a passive hinge is damped, held inside its limits,
  latched open or shut by Interact, swung by a player walking into its leaf, and held still while its lock is on; the
  moving joints are driven by a motor (milestone 2, below).
- A body that stays still for 40 ticks sleeps and costs nothing until a hit, an edit, a driver or a nearby body wakes it.
- Players are never moved by physics here: they are pushed out of construct parts (or stand on top of them, carried
  along by the body and by any joint under their feet), and drivers and passengers are pinned to their seats (and drawn
  as part of the construct: "Seats" below). A moving
  part that closes on a player shoves them along and, faster than 4 m/s, hurts them (run-over damage, below).

### Damage (decision 6)

Every part has its own **health** and **armour** (a flat amount taken off every hit). Bullets hit the part they reach
(construct parts stop bullets and shield what is behind them); blasts (grenades, launchers, kill streaks, explosive props)
hurt every part in range, falling off with distance, and push the bodies. At zero health a part either **breaks off**
(`"onBreak": "detach"`, the default) or **stops working** (`"disable"`: the default for engines, tanks and seats; a wrecked
tank loses its fuel). Shooting a cheap fuel line or a suspension disables a vehicle without wrecking it: the loot loop's
core (salvage itself is milestone 2).

## The data

### Part files: `Constructs/Parts/*.json`

One part per file, loaded at start-up; ids follow the sorted file names. A bad file is refused with a reason (the rest
still load): `CallOfSlop --check-construct-parts` prints every part and blueprint, or the reason it was refused.

```json
{
  "format": "call-of-slop-construct-part/1",
  "name": "Suspension",
  "description": "A sprung wheel mount: bolt it to a frame's side, then a wheel onto it.",
  "role": "suspension",
  "tool": "wrench",
  "attachSeconds": 1.5,
  "mass": 15, "health": 110, "armour": 3, "material": "steel",
  "color": "#b08a2e",
  "boxes": [
    { "size": [0.3, 0.3, 0.3] },
    { "size": [0.12, 0.44, 0.12], "color": "#d0b050", "detail": true }
  ],
  "sockets": [
    { "name": "mount", "at": [-0.15, 0, 0], "dir": "-x" },
    { "name": "axle", "at": [0.15, 0, 0], "dir": "+x", "kind": "axle" }
  ],
  "suspension": { "travel": 0.3, "frequency": 2.2, "damping": 0.7 }
}
```

| Field | Meaning |
| --- | --- |
| `name` | Unique, 1-32 ASCII characters. Blueprints, maps and the rack name parts by it. |
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fuelline`, `hinge`, `door`, `foundation`; milestone 2: `joint`, `transmission`, `differential`, `logic`. |
| `tool` | `wrench` (bolted) or `welder` (welded). The linking tool never fits parts. |
| `attachSeconds` | Time to fit (default 1.5 wrench, 2.5 welder); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working; the default for engines, tanks, seats, transmissions and differentials). |
| `boxes` | Up to 16: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge). Two sockets mate when their kinds match and they face each other. |
| `engine` | `force` (N, shared by the touching wheels), `topSpeed` (m/s), `burnRate` (litres a second at full throttle). |
| `wheel` | `radius`, `grip`. Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio). Needs an `axle` socket. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `seat` | `at`: where the sitter's feet go. |
| `hinge` | `axis` (a direction), `pivot`, `min`/`max` (degrees), `leaf` (the leaf socket: what attaches there swings). |
| `joint` | Milestone 2, a moving joint: `type` (`motor`, `turntable`, `piston`, `steering`), `axis`, `pivot`, `min`/`max` (degrees; a piston's are metres, `min` 0 and `max` its travel up to 4 m; a turntable without them turns forever), `leaf` (a struct socket: anything fitted there moves, a door's `leaf` socket too), `speed` (degrees or metres a second), `strength` (N m, or N for a piston). |
| `logic` | Milestone 2, a logic part: `kind` (below), and for the kinds with a setting `settings` (the values it cycles through), `labels` (what the HUD shows; an input reader's are its keys), `setting` (the starting index), `settingName`. |

Milestone 1 shipped 13 parts: Frame Block, Frame Beam, Panel, Corner, Seat, Wheel, Suspension, Engine, Fuel Tank, Fuel Line,
Hinge, Door and Foundation. Milestone 2 adds 25: Deck Plate; Motor Hinge, Turntable, Piston, Steering Pivot; Transmission,
Differential; Button, Lever, Switch, Pressure Plate, Input Reader, Speed Sensor, Proximity Sensor, AND Gate, OR Gate, NOT
Gate, Timer, Delay, Toggle, Compare, Scale, Lamp, Horn and Brake.

### Blueprints: `Constructs/Blueprints/*.json`

A blueprint is a **build script**: each step fits a part onto a socket of an earlier step's part, through exactly the
same rules a player builds by, so a blueprint that loads proves the rules can build it.

```json
{ "format": "call-of-slop-construct-blueprint/1", "name": "Gate", "steps": [
  { "part": "Foundation" },
  { "part": "Frame Block", "on": 0, "socket": "top01", "by": "-y" },
  { "part": "Hinge", "on": 1, "socket": "+x", "by": "base" },
  { "part": "Door", "on": 2, "socket": "leaf", "by": "hang", "spin": 0 }
] }
```

A step may carry `"setting": n` (a logic part's starting setting), and a blueprint may list `"wires": [[from step, to
step], ...]`, each checked by the linking tool's rules. Shipped: **Buggy** (23 parts: rear-wheel drive through a
transmission and a differential, front wheels on steering pivots), **Wired Buggy** (30 parts, 6 wires), **Gate** (6
parts), **Motor Gate** (8 parts, 2 wires), **Lift** (7 parts, 3 wires) and **Turret** (6 parts, 1 wire).

### The map's `"sandbox"` section

```json
"sandbox": {
  "rack": [-22, 23, 180],
  "rackParts": ["Frame Block", "Frame Beam", "Seat", "Wheel"],
  "constructs": [ { "blueprint": "Buggy", "at": [-8, 4], "yaw": 0 } ],
  "loose": [ { "part": "Wheel", "at": [-14, 20] } ]
}
```

`rack` is `[x, z, yaw]`; `rackParts` defaults to every part; `"partsMenu": false` turns the debug parts menu off (it is
on by default). Unknown parts or blueprints refuse the map with the reason.
Only the server reads the section (clients get every construct from the server), so it is not part of the fingerprint.
The Sandbox mode is offered only on maps with this section.

## Network (protocol 79)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid with their logic setting and signal,
  links, joints with their type, lock and wired flags, wires, pose: about 20 bytes plus 14 per part and 2 per wire),
  sent when it changes (an edit, a break, a split, a wire, a setting) and to each joiner; also part health batches, a
  construct gone, and "clear all" at a new match.
- `Msg.CPose` (unreliable sequenced, 20 Hz): the bodies that move (32 bytes plus 4 per joint, the angle and speed, plus
  a byte per signal part when it has logic), any whose signals changed (a lamp switched on in an anchored base), plus
  four sleeping ones in turn so a lost packet never leaves one wrong for long. Clients blend between poses and fit their
  wheels to the ground themselves.
- `Msg.CAct` (reliable): a client's request (tool, attach, place, remove, cancel, use, stand, rack, drop, pick up; wire,
  unwire, a setting, a seated player's fire/aim/reload keys; protocol 80: `SeatLook`, a seated player's seat-relative
  look, sent unreliably); the server re-checks everything and answers a refusal with its reason. Protocol 79: `Rack` carries a pick byte (0: the rack's next kind, at the rack; n: part id n - 1, from the
  debug parts menu, anywhere) and `Blueprint` (a blueprint index) builds one in front of the player.
- The Sandbox mode-state tail (~10 Hz, version 2): every player's tool (the linking tool too), held part, seat and edit in
  progress.

## Performance and caps

At most 64 bodies, 96 parts a construct, 1,200 parts in all, 8 joints and 12 wheels a construct (128 joints in all),
64 wires a construct (512 in all), 4 inputs a part, wires up to 8 m, 40 hull points a body, 1,024 contacts a tick. Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation** (with the turret turning, the lift and the motorised gate moving and their logic running);
the yard's constructs take about 0.1-0.4 ms a tick in the container. The logic step is a few spans on the stack. Sleeping bodies cost
nothing. Other modes never touch any of it (every hook returns at once outside Sandbox; the seeded mode digests are
unchanged).

## Self-tests

The **Constructs** section (`Source/App/SelfTestConstructs.cs`): part files load and 12 kinds of bad file are refused
for the right reason; the buggy settles level on four wheels, drives 30+ m in 3 s, turns right, brakes, and climbs the
ramp onto the deck; a shot-off fuel line stops it; a panel's armour shrugs off a small hit; a broken suspension drops
its wheel as a wreck; a blast damages parts; the gate's door opens, shuts and swings when walked into while the anchored
gate stays put; a car built part by part through the edit rules (bare hands refused, the wrong tool refused, timed
fits, ghost sockets offered, a wheel taken off into the hand, sit and stand); a client copy from the wire matches and a
truncated packet is refused; no allocation per tick; and a real host and client: mirror, sit and drive with the client's
own keys, stand, a bare-hands build refused with its reason, a broken part mirrored, a late joiner gets everything.
"Mode modules" plays a short bot match in Sandbox on Construct Yard.

The **Construct logic** section (`Source/App/SelfTestConstructLogic.cs`, milestone 2): bad joint, logic and blueprint
files refused for the right reason; the Lift's piston reaches and holds its 2 m with two riders, sinks when overloaded
and comes down on its button; the Motor Gate's door opens and shuts on its button, can't be pushed or opened by hand;
the Turret turns both ways on its lever and holds in the middle; the buggy's steering pivots follow the driver inside
their limits; a locked hinge stays shut; a bench of logic parts on a foundation (AND, OR, NOT, a chain of three NOTs that
takes exactly three ticks, a NOT wired into itself flipping every tick, a 1 s timer, a 0.5 s delay, a button through a
toggle into a lamp, a lever through scale and compare, an input reader on a seat's fire key, a proximity sensor); the
linking tool's rules (bare hands and the wrong tool refused, eleven refusals with their reasons, cutting wires, cycling
a setting); a shot transmission and a shot differential stop the buggy; a switch wired into the engine drives it with
nobody in the seat and a brake part stops it; the wired buggy's horn and lamps follow the driver's keys; and in a real
game the lift raises the player standing on it, a passenger rides along in the second seat, and a player in the
buggy's way is hurt. "Constructs"' loopback now also checks the client and a late joiner mirror the joints, wires and
signals.

The **Sandbox parts menu** section (`Source/App/SelfTestSandboxPartsMenu.cs`): the map's `partsMenu` flag reads (on by
default, a bad value refused); in an offline Sandbox game the menu's request hands the chosen part by id to the player
anywhere, an unknown id is refused (the hands kept), the rack still refuses from afar and hands out its next kind at the
rack, a Turret spawns in front of the player and a bad blueprint index is refused, and the map flag turns both off; the
window draws headless (every part listed, a search narrows it, a pick puts the part in hand with its tool out and closes
the window) with no ImGui error and no allocation per frame while open; a real client's part by id and blueprint go over
the wire; and outside Sandbox a blueprint spawn is refused.

The **Sandbox tool wheel** section (`Source/App/SelfTestSandboxToolWheel.cs`): the direction-to-segment maths (every
segment's middle and both edges for 8, 4, 3, 6 and 1 segments, the wrap-around at the top, the dead zone, a pointer that
isn't a number), the pointer held at its reach so one flick turns it; the key (an unshared middle mouse button; binds
version 9 files get it, one that already used the button keeps it); in an offline Sandbox game a scripted
hold-flick-release takes out the welder (east), wrench (north), linking tool (west) and hands (south), the current tool is
marked when it opens, letting go in the dead zone cancels, the hoist says "coming later" and changes nothing, a menu and
death close it without choosing (and the still-held key doesn't reopen it), and it doesn't open in a seat; 240 headless
frames with the pointer going round draw without an ImGui error and allocate nothing; and a real client's pick reaches the
server.

`--sandbox-shots` writes pictures of the yard, a buggy, the ghost sockets, a driven buggy, its front wheels steering,
the gate open, the motorised gate open, the lift up, the turret turned, the wired buggy with its lamps on and its
wires (the linking tool's view), a broken buggy, the debug parts menu (open, searched, and a buggy spawned from it), and the tool wheel held open (on the linking tool, then on the greyed hoist) (`smoke_sandbox_*.bmp`); it runs under xvfb like `--light-shots`.

## Decisions made for milestone 1 (open questions it touches; the developer may change them)

1. **Joints**: only the passive hinge ships, as a reduced-coordinate angle on its body; the joint record is ready for a
   motor (turntable) and a slide (piston), which milestone 2 adds with the linking tool and logic parts.
2. **What players start with**: a gun loadout as usual, both tools always available (keys 3 and 4), and an endless
   parts rack on the map. No resources, crafting or unlocking yet.
3. **Destroyed parts** break off as wrecked debris (scrap to salvage later); engines, tanks and seats are disabled
   instead. Armour is a flat reduction per hit (small arms can't hurt heavy plate).
4. **Drivers don't shoot**; milestone 2's passengers don't either (turrets come with construct weapons).
5. **Unpowered vehicles hold still** below 1.5 m/s (a parking brake), so parked and dead cars don't creep.
6. Players are pushed by constructs but never push them (except passive doors); getting run over hurts since milestone 2.
7. Parts snap to sockets in 90 degree steps on a 1 cm grid; there is no free placement.
8. Constructs reset with the match (no saving); everything is in memory on the host.

## Milestone 2: joints, logic and the drivetrain

### Joints

Every joint is still one number on its body (reduced coordinates: an angle, or a piston's extension), so the two
pieces it links, the parent segment and the segment hanging on its leaf, can never drift apart or explode, and a joint
costs a few multiplies a tick. A **moving joint** has a motor: each tick it wants a speed (a turntable: its input times
its speed; the rest: towards the position its input asks for, at most its speed) and pushes for it with at most its
strength, against the **load**: gravity on everything the joint carries (its segment, every segment hanging on that,
and 85 kg for each player standing on it) along its motion. A lift whose load is more than its strength sinks; one under
it holds where it is. Limits clamp it; a value that stops being a number is put back to rest. The construct's mass and
centre of mass stay those of the rest pose (cheap; a swinging arm doesn't rock its base yet).

| Joint | Input (the wires into its part, added up) | Unwired |
| --- | --- | --- |
| Passive hinge (`Hinge`) | On: **locked** (Interact refused, pushes ignored) | Swings, latched open or shut by Interact |
| Motor Hinge | 1: to `max`, 0: back to rest, -1: to `min` | Interact opens and shuts it |
| Turntable | -1..1 of its speed (a lever: back, hold, forward) | Holds still |
| Piston | 0..1 of its travel | Interact pushes it out and pulls it in |
| Steering Pivot | -1..1 of its limit | Follows the driver's steering |

Wheels steer only on steering pivots now (milestone 1 steered every wheel ahead of the centre of mass): a suspension
bolted to a pivot's leaf, and the wheel on it, turn with it, and its axle sets the tyre's direction. Caps: 8 joints a
construct, 128 in all.

### Logic

A logic part puts out a signal: on/off (1/0) or a number. **Every wire carries the value its source had last tick**:
each tick works out every part's next value from last tick's values, then they all change at once. The order parts are
worked in never matters (bodies in list order, parts in index order, on the server), a chain of N parts takes N ticks
(a 60 Hz tick is 17 ms), and loops are fine: a NOT wired into itself is a clock, on one tick and off the next.

| Kind | Puts out | Setting (R with the linking tool) |
| --- | --- | --- |
| Seat (`seat` role) | 1 while someone sits in it | - |
| `button` | 1 for half a second after Interact | - |
| `switch` | Interact: 1 or 0 | - |
| `lever` | Interact moves it: middle 0, forward 1, middle 0, back -1 | - |
| `plate` (Pressure Plate) | 1 while a player stands on it | - |
| `reader` (Input Reader) | 1 while the sitter of the seat wired into it (unwired: the driver's seat) holds its key | key: fire, aim, reload, jump, sprint, forward, back, left, right |
| `speed` (Speed Sensor) | its construct's speed, m/s | - |
| `proximity` | 1 while a player is within its range | 3, 6 or 12 m |
| `and` / `or` / `not` | every input on / any on / none on | - |
| `timer` | while enabled (nothing wired in, or an input on): 1 and 0, each for half its period | 0.5, 1, 2, 5 s |
| `delay` | its input, once the input has held its new value for the delay (shorter blips are lost) | 0.25, 0.5, 1, 2 s |
| `toggle` | flips each time its input comes on | - |
| `compare` | 1 when its inputs add up to more than the threshold | 0.5, 1, 5, 10 |
| `scale` | its inputs added up, times the factor | x -1, x 0.5, x 2, x 0.1 |
| `lamp` / `horn` / `brake` | (no output) lights / beeps / brakes the wheels while its input is on | - |

Outputs act the same tick they are worked out: joints (above), the **engine** (a signal wired into it is its throttle,
-1..1, and it drives with nobody in the seat), lamps, horns, brakes, and a passive hinge's lock. A part takes at most 4
inputs (an input reader, a timer and a delay 1).

### The linking tool

Key 5. Fire on an output part, then fire on an input part of the **same construct**: the server checks and wires them,
or refuses with the reason: "take out the linking tool (5) to wire" (bare hands, a gun, the wrench or the welder),
"get out of the seat first", "too far away", "a Foundation has no output", "a Switch takes no input", "an input reader
reads a seat: wire it from one", "those are already wired", "a Delay takes at most 1 input", "a wire reaches at most
8 m", "at most 64 wires a construct". Aim on a part cuts every wire on it; R on a logic part cycles its setting. Wires
are drawn only while the linking tool is out (through the parts, green while their signal is on, a yellow block at the
input end). Wiring is instant (fitting a part is what takes time).

### Passengers, riding and running people over

- The first working seat of the body drives; any other seat carries a **passenger**, pinned to it like the driver. A seat
  holds one player (`SitterPlus1` on the part, so it survives parts being renumbered); a seat that breaks or falls away
  stands its sitter up. Seated players' fire, aim and reload keys go to the server (`CAct.SeatKeys`) for input readers.
- A player standing on a part is **carried** by the body's motion and, sideways, by every joint under their feet (a
  turntable, a swinging deck); a rising lift lifts them by the step-up rule. They count towards that joint's load.
- **Run-over damage**: a moving part (a car, a swinging arm) that closes on a player faster than 4 m/s does
  `(speed - 4) x 9 x sqrt(mass / 500)` (the mass factor 0.4 to 2.5) damage, at most once in 0.6 s, and knocks them along
  with it; the driver gets the kill (shown as a melee kill), or nobody for a driverless construct.

### Seats: riders as part of the construct

- **Seat types** (`SeatKind`, from where the seat is): the drive seat is the **driver's**; a seat on a moving joint's
  segment (a turntable, a hinge) is a **gunner's**; any other is a **passenger's**. The **Gunner Turret** blueprint is
  the Turret with a gunner's seat on its arm.
- **The seat frame.** Every frame, on the host and every client, after the render states and before the camera
  (`Game.UpdateSandboxSeats`, GameSandbox.cs), each seated player is put in their seat exactly as the construct is drawn
  this frame (`ConstructRender.SeatPose`: the body's blended pose, every joint up the seat's chain blended, the seat's
  turn): the feet at the seat's `at` point and the whole body in the seat's frame, so it tilts and turns with the
  construct and with a turntable or hinge under the seat. There is nothing to lag, jitter or slide: the rider is a
  seat-local pose composed with the construct's own drawing. (A client blends the construct poses there too, before the
  camera is set.) The server still pins the simulated player to the seat each tick for hit boxes and reach.
- **The seated pose** (`LegAnimator` seated legs): hips on the cushion, thighs forward, knees bent, feet forward on the
  floor, leaning back a little; no gait, no foot rays, no footsteps or step sounds, no head bob, no sprint arms. The upper
  body turns towards the look (driver 26 degrees, passenger 46, gunner 63 either way). The **driver** holds the
  **steering wheel** (drawn in front of the drive seat, turned by the steering) with both hands and has no gun; a
  **passenger** rests the gun across the lap; a **gunner** holds it up at their look.
- **The look** is relative to the vehicle: the local view turns with the seat (like a turning platform's), and the look
  stays within 100 degrees of the seat's facing for the driver, 120 for a passenger, all the way round for a gunner.
  The camera is at the seat's head (the eye height along the seat's up).
- **Network (protocol 80).** A seated player's snapshot entry carries the seat (construct id and seat part, in the
  mantle extension with mantle ticks 0) and a yaw relative to the seat's facing instead of a world one; a client
  composes it with its own drawing of the construct. The seat-relative look comes from the seated client itself
  (`CAct.SeatLook`, unreliable, on a change, at most ~30 a second and every 0.25 s), so it is exact however fast the seat
  turns; until the first one arrives the server works it out from the world look. The world position stays in the entry
  (a fallback for a construct the client doesn't have yet).
- **Getting in and out** keeps its rules (Use on a free seat; F gets out, beside the seat if clear).
- **Dying seated**: the ragdoll starts in a seated pose at the seat, with the construct's velocity there, and goes limp
  (no death reaction).
- The **"Seated riders"** self-test: in a game on the yard a driver and a passenger in the wired buggy and a gunner on
  the Gunner Turret stay within 4 mm of their seats' local offsets (feet and eyes, every frame between ticks) while the
  buggy drives at up to 12 m/s and turns 140 degrees and the turret turns; no walk cycle or footsteps; the look limits; a
  seated ragdoll; no allocation; over a loopback two clients agree on the seat, the offset and the seat-relative look.
  `--sandbox-shots` writes `smoke_sandbox_seated_*.bmp` (riders, the driver close up, the passenger, the driver's view,
  the gunner).

### Decisions made for milestone 2 (the developer may change them)

1. **Joints stay reduced coordinates**: a joint is a number on one body, not a constraint between two free bodies, so it
   can't come apart or explode; its load is gravity on what it carries, and the body's mass properties use the rest pose.
2. **Every wire takes one tick** (the default the brief suggested): order never matters, loops are clocks. Chains of
   many gates are a little slower (17 ms a part).
3. **Wires join parts of one construct only** (a button on a wall can't run a separate gate yet), up to 8 m long.
4. **Steering is a joint**: wheels steer only on steering pivots, which follow the driver unless something is wired in.
5. **The drivetrain is strict**: an engine drives nothing without a transmission touching it and a differential it feeds
   (the scripted build test fits both); only wheels on a differential are driven, the rest roll.
6. **Seats**: the first seat drives, every other is a passenger seat. Passengers don't shoot.
7. **Logic settings** are changed with the linking tool's R (no separate configure tool); controls (buttons, switches,
   levers) work with any hands, as Use.
8. A **pressure plate** is pressed by players only (not by constructs or loose parts).
9. **The welder and linking tool** sit on 4 and 5, the kill-streak keys (Sandbox has no streaks; the Weapon slot 4
   binding still works too), so no new key binding was added.
10. **Run-over kills** show as melee kills and credit the driver.

## Next: milestone 3 and later

- **Salvage**: taking surviving parts off wrecks into an inventory; scrap from destroyed parts; debris expiry.
- **Construct weapons** (decision 10), mounted fixed or on turntables (the Turret is waiting for one), fired by input
  readers, sharing the weapon builder's ammo flags.
- **Resources** and production parts, **enemies** that drive and defend constructs, **factions**.
- Joints between separate constructs (trailers, a wall button wiring a gate), wires between constructs, joint reactions
  rocking the body, motors that drain fuel or a battery (decision: electrical systems).
- Saving constructs with the world, then the large world, streaming and points of interest.
- More parts (bumpers, fenders, gauges, a battery and electrical wiring), gears in the transmission, and a bigger map built
  over several iterations, as the developer asked.

## Milestone 1's list, as milestone 2 left it

- Done: the linking tool and logic parts (seat -> input reader -> piston), motorised hinges, turntables, pistons, steering
  as a joint, transmissions and differentials as parts you can shoot, passengers, standing on a moving deck, run-over
  damage, lights (the Lamp).
- Still to come (above): salvage, construct weapons, saving, the large world, debris expiry, more parts, a bigger map.
