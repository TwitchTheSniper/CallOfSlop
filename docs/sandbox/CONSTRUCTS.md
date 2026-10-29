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
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |
| Fire / Aim / R (seated) | Held keys the construct's **input readers** can read (drivers and passengers don't shoot). |
| Linking tool | Fire on an output, then fire on an input: a wire. Aim on a part: cut its wires (or let go of a wire in progress). R on a logic part: its next setting. |

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
  along by the body and by any joint under their feet), and drivers and passengers are pinned to their seats. A moving
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

`rack` is `[x, z, yaw]`; `rackParts` defaults to every part. Unknown parts or blueprints refuse the map with the reason.
Only the server reads the section (clients get every construct from the server), so it is not part of the fingerprint.
The Sandbox mode is offered only on maps with this section.

## Network (protocol 78)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid with their logic setting and signal,
  links, joints with their type, lock and wired flags, wires, pose: about 20 bytes plus 14 per part and 2 per wire),
  sent when it changes (an edit, a break, a split, a wire, a setting) and to each joiner; also part health batches, a
  construct gone, and "clear all" at a new match.
- `Msg.CPose` (unreliable sequenced, 20 Hz): the bodies that move (32 bytes plus 4 per joint, the angle and speed, plus
  a byte per signal part when it has logic), any whose signals changed (a lamp switched on in an anchored base), plus
  four sleeping ones in turn so a lost packet never leaves one wrong for long. Clients blend between poses and fit their
  wheels to the ground themselves.
- `Msg.CAct` (reliable): a client's request (tool, attach, place, remove, cancel, use, stand, rack, drop, pick up; wire,
  unwire, a setting, a seated player's fire/aim/reload keys); the server re-checks everything and answers a refusal with
  its reason.
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

`--sandbox-shots` writes pictures of the yard, a buggy, the ghost sockets, a driven buggy, its front wheels steering,
the gate open, the motorised gate open, the lift up, the turret turned, the wired buggy with its lamps on and its
wires (the linking tool's view), and a broken buggy (`smoke_sandbox_*.bmp`); it runs under xvfb like `--light-shots`.

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
