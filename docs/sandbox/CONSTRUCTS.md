# Constructs and the Sandbox mode: milestone 1

The sandbox survival mode (board item dev-idea-constructs-cs01; the full design and the developer's 16 decisions are in
the project doc `claude/sandbox-construct-mode.md`) is being built in milestones. **Milestone 1 is the construct core,
proven on one small test map**: parts, tools in hand, ghost sockets, one rigid body per welded group, a working
drivetrain, a passive hinge, per-part damage with wrecks, and all of it server-authoritative over the network. There is no
large world, streaming, factions, enemies, points of interest, resources, scanning, terrain digging or construct weapons
yet.

## Playing it

Host a game on **Construct Yard** (Dev pack) and pick **Sandbox** (a Dev-only mode, like the template). The yard has two
buggies, an anchored gate, a parts rack (the yellow shelf in the north-west corner) and some loose parts.

| Key | What it does in Sandbox |
| --- | --- |
| 3 / 4 | Take out the **wrench** / the **welder** (press again to put it away). 1 or 2 go back to your guns. |
| F | At the rack: take a part (press again for the next kind). On a seat: sit. On a door or hinge: open or shut it. On a loose part: pick it up. In a seat: get out. |
| Fire (hold) | With a tool out and a part in hand: fit the part on the ghost socket under the crosshair (or set it down on the ground as a new construct). Letting go early stops it. |
| R | Turn the part on its socket (the next way round that fits). |
| Aim (hold) | With a tool out: take the part under the crosshair off (into your hand, or dropped if your hands are full). |
| T | Drop the part in your hand. |
| W S A D, Space | Driving: throttle, reverse (brakes first), steering, brake. |

Editing always needs the right tool in hand (decision 2): **welder** for frames, panels, corners and foundations,
**wrench** for everything bolted (seats, wheels, suspensions, engines, tanks, fuel lines, hinges, doors). With a gun out
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

A construct drives when it has a working **seat** with a driver, a working **engine** in the body, **fuel** reaching the
engine (a working tank linked to the engine directly, or through a chain of working **fuel lines**), and **wheels on
working suspensions**. Every such wheel is driven (milestone 1 has no transmission or axle parts). A missing or broken
link stops it, and the HUD says which: "no fuel line from the tank to the engine", "the engine is wrecked"...

### Physics

`Source/Constructs/ConstructPhysics.cs`, server only, at the fixed 60 Hz tick:

- Rigid bodies with box inertia, integrated with gravity (the game's own gravity and the host's gravity setting).
- Against the map: up to 40 **hull points** per body (part corners, the outermost kept), each probed with a ray down and
  a ray along its motion, giving speculative contacts solved with sequential impulses and friction.
- Between constructs: hull points of one against the part boxes of the other.
- **Raycast wheels**: a spring-damper along each suspension (stiffness from the body's mass and the suspension's
  frequency), side grip, drive force shared by the touching wheels, brakes, a friction circle, and tyre forces applied
  most of the way up to the centre of mass (an arcade anti-roll bar) so a buggy turns without rolling over.
- **Hinges**: passive, damped, held inside their limits; Interact latches them open or shut; a player walking into a
  door leaf swings it.
- A body that stays still for 40 ticks sleeps and costs nothing until a hit, an edit, a driver or a nearby body wakes it.
- Players are never moved by physics here: they are pushed out of construct parts (or stand on top of them, carried
  along), and a driver is pinned to the seat.

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
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fuelline`, `hinge`, `door`, `foundation`. |
| `tool` | `wrench` (bolted) or `welder` (welded). |
| `attachSeconds` | Time to fit (default 1.5 wrench, 2.5 welder); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working). |
| `boxes` | Up to 16: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge). Two sockets mate when their kinds match and they face each other. |
| `engine` | `force` (N, shared by the touching wheels), `topSpeed` (m/s), `burnRate` (litres a second at full throttle). |
| `wheel` | `radius`, `grip`. Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio). Needs an `axle` socket. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `seat` | `at`: where the sitter's feet go. |
| `hinge` | `axis` (a direction), `pivot`, `min`/`max` (degrees), `leaf` (the leaf socket: what attaches there swings). |

Milestone 1 ships 13 parts: Frame Block, Frame Beam, Panel, Corner, Seat, Wheel, Suspension, Engine, Fuel Tank, Fuel Line,
Hinge, Door and Foundation.

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

Shipped: **Buggy** (19 parts) and **Gate** (6 parts).

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

## Network (protocol 77)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid, links, joints, pose: about 20 bytes
  plus 12 per part), sent when it changes (an edit, a break, a split) and to each joiner; also part health batches,
  a construct gone, and "clear all" at a new match.
- `Msg.CPose` (unreliable sequenced, 20 Hz): the bodies that move (31 bytes plus 2 per joint each), plus four sleeping
  ones in turn so a lost packet never leaves one wrong for long. Clients blend between poses and fit their wheels to
  the ground themselves.
- `Msg.CAct` (reliable): a client's request (tool, attach, place, remove, cancel, use, stand, rack, drop, pick up); the
  server re-checks everything and answers a refusal with its reason.
- The Sandbox mode-state tail (~10 Hz): every player's tool, held part, seat and edit in progress.

## Performance and caps

At most 64 bodies, 96 parts a construct, 1,200 parts in all, 8 joints and 12 wheels a construct, 40 hull points a
body, 1,024 contacts a tick. Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation**; the yard's constructs take about 0.1-0.4 ms a tick in the container. Sleeping bodies cost
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

`--sandbox-shots` writes pictures of the yard, a buggy, the ghost sockets, a driven buggy, the gate open and a broken
buggy (`smoke_sandbox_*.bmp`); it runs under xvfb like `--light-shots`.

## Decisions made for milestone 1 (open questions it touches; the developer may change them)

1. **Joints**: only the passive hinge ships, as a reduced-coordinate angle on its body; the joint record is ready for a
   motor (turntable) and a slide (piston), which milestone 2 adds with the linking tool and logic parts.
2. **What players start with**: a gun loadout as usual, both tools always available (keys 3 and 4), and an endless
   parts rack on the map. No resources, crafting or unlocking yet.
3. **Destroyed parts** break off as wrecked debris (scrap to salvage later); engines, tanks and seats are disabled
   instead. Armour is a flat reduction per hit (small arms can't hurt heavy plate).
4. **Drivers don't shoot** and passengers don't exist yet (one seat drives); turrets come with construct weapons.
5. **Unpowered vehicles hold still** below 1.5 m/s (a parking brake), so parked and dead cars don't creep.
6. Players are pushed by constructs but never push them (except doors); getting run over does no damage yet.
7. Parts snap to sockets in 90 degree steps on a 1 cm grid; there is no free placement.
8. Constructs reset with the match (no saving); everything is in memory on the host.

## Milestone 2 should take on

- The **linking tool** and logic parts (seat -> input reader -> piston), motorised hinges, turntables, pistons, steering
  as a joint; transmissions and axles as parts (drivetrain links you can shoot).
- **Salvage**: taking surviving parts off wrecks into an inventory; scrap from destroyed parts.
- **Construct weapons** (decision 10), mounted fixed or on turntables, sharing the weapon builder's ammo flags.
- Players riding on moving constructs properly (passengers, standing on a moving deck), run-over damage.
- Saving constructs with the world, and then the large world, streaming, points of interest and the first enemies.
- Debris expiry (today wrecks stay for the match; at the 64-body cap new debris is simply not made), more parts (bumpers, fenders, lights, gauges),
  and a bigger map built over several iterations, as the developer asked.
