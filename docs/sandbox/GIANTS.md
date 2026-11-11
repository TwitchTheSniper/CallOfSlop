# Giants: walkers and moving bases

History (phase records): GIANTS-HISTORY.md. Read it only when you need why something was built this way.

The track that lets players build and drive giant moving bases, starting with one ~100 m six-legged walker. The user's
decisions (2026-11-10) are the design; this file is the code guide and the record of what each phase did.

## The decisions (short)

- **A leg is one part**, fitted with the welder: hip, segments, knees, ankle, foot and actuators built in, rigged as one
  mechanism. Kinds (mech, digitigrade, insect), size tiers (small to Titan), tunable segment lengths, 2 to 4 segments.
  Power: a hydraulic circuit, electric power or a drive shaft, each with its own strength. Decorations welded to a segment
  ride it (mass and hit area only, never the rig).
- **The gait brain** (walk assist): the player never makes a walk cycle. It finds the legs, picks a gait from their count
  (a hexapod: tripod), finds footholds by a ray down through the world, solves the legs by analytic IK, keeps the centre of
  mass inside the planted feet and re-plans after a leg goes. Profiles: Simple (no settings), Basic, Advanced.
- **Hybrid physics**: a locomotion controller moves the body; the legs are IK-placed with real foot contacts; leg damage
  cuts support and speed; too few legs and it collapses to real physics.
- **Control**: seat driving (move, turn, strafe, height), later an autopilot to a destination.
- Speed comes from the build (leg size, power, mass). Terrain: steps over obstacles, slopes, wades, later crushes what is
  underfoot. Ride feel from parts (thumps, shake; stabilisers: phase 4). Size target 500 m and more; damage per part as today.

## Phase 1 (branch `feat/giant-walker`)

### What limits a 100 m construct (the survey)

What was read: `docs/sandbox/CONSTRUCTS.md` (what a construct is, rebuilds, physics, seats, size classes, limits),
`docs/perf/LIMITS.md`, `Source/Constructs` (Construct, ConstructPhysics, ConstructDeck, ConstructDrive, ConstructModules,
ConstructBroadphase, ConstructBuild), `Source/Render/ConstructRender*.cs`.

| Area | What there is | At 100 m | At 500 m |
| --- | --- | --- | --- |
| Float precision | World positions are `float` (`Construct.Pos`, parts on a 1 cm grid in the body frame); space has its own doubles (`SpacePos`) | Fine: a float near 1 km is good to 0.06 mm | Fine within a few km of the origin; past ~8 km a millimetre goes, which the map sizes don't reach |
| One part's size | `ConstructParts.MaxPartReach` 200 m from its origin, 256 boxes, 255 sockets, mass 50,000 t, health u24 | A titan leg reaches 48 m: fine | A leg longer than 200 m needs the reach cap raised, or legs built of several parts |
| Parts a body | 16,000 (`Construct.MaxParts`); big bodies rebuild by the journal (plain parts) | A walker of a few thousand parts is fine | A town on a walker reaches it; fused units ([Fusing](constructs/building-and-tools.md#fusing)) are the answer there |
| Joints | 255 a body (a segment is a byte) | Legs are one part each with their own pose, so they spend no joint | The same: the leg rig is not built of joints, by design |
| Collider and solver | Every solid box in a tree per body; sequential impulses; a speed cap of 60 m/s; the broadphase grid is 8 m cells with a 1.2 m margin | A 128 m chassis is one body with a few boxes a part: cheap | A body's sphere (`Radius`) wakes everything in it: a 500 m walker keeps a town of neighbours awake. Wants a box broadphase test, not the sphere |
| Wire reach | 8 m centre to centre (`ConstructBuild.WireReach`) | A capital part's centre is 8 m inside its own cell, so nothing could be wired to it. Fixed: a part bigger than the reach is wired to the nearest point of its bounds (`WireSpan`) | The same rule holds |
| Riders and the ride frame | Riders stand on the body (`ConstructRideMove.cs`), seats pin sitters; the contraption space measures faces | Works as for any big body (carriers, decks) | The ride feel (thumps, shake, stabilisers): phase 4 |
| Network | Bodies are poses (`ConstructNet`), joints as angles; parts and links once | The feet need nothing on the wire: a client poses them itself (below) | The same |
| Drawing and LOD | Merged meshes for static parts; a big body (`BatchMinParts`) is drawn whole out to 3 km, never as one box; joints and moving parts box by box | The legs are drawn as boxes each frame (about 20 a leg) | Many walkers: instance the leg boxes (they already go through one batch) |
| Ground | `TerrainData` answers for dug and streamed ground; the world query's ray reads it | Footholds use the server's ground ray (decks of other bodies too) | The same |

### The leg part (`Constructs/Parts/Walkers`, from `tools/constructs/leg_parts.py`)

Role `leg`, one file a kind and tier (12 now: Small, Medium, Large and Titan; Mech, Digitigrade and Insect). The part's
boxes are its **hip housing** (one cell of its size class, solid; an actuator cap drawn proud of the outer face), with a
`mount` socket on its -x face and a `top` socket. The `leg` block:

| Key | Meaning |
| --- | --- |
| `type` | `mech` (knee forward, foot under the shin), `digitigrade` (knee forward, the last segment swept back on its toes), `insect` (a level coxa out from the hip, the knee up and out, the last segment straight down) |
| `tier` | A label: `small`, `medium`, `large`, `titan` |
| `segments` | Lengths in metres, hip first, 2 to 4 (an insect's first is its coxa) |
| `width`, `foot` | The thickest segment's side; the foot pad's half width (m) |
| `hip` | The hip's pivot in the part's frame (default its origin) |
| `load` | kg the leg carries at full pace |
| `stepTime` | Seconds a step takes in the air, unloaded |
| `stepHeight`, `stand`, `spread` | Shares of the reach: how high a foot lifts, the hip's standing height, how far out the foot rests (stand and spread together within 0.9) |
| `power` | Strength on `hydraulic`, `electric` and `shaft` power (data only in phase 1: the legs walk on their own actuators) |
| `jointColor` | Knuckles and foot |

The leg takes a Driving Module's drive link on its `drive` port (and has a `height` port for later): a seat wired through
a Driving Module to any leg is the walker's drive seat (`ConstructModules.Build`, as for a drivetrain).

### The gait brain (`Source/Constructs/ConstructWalk.cs`)

- **The legs** (`Construct.BuildLegs`, on every rebuild after the wheels): every leg part on the body's own frame, with its
  hip and resting foot in the body frame, sorted by angle round the centre of mass, so the planted feet in that order are
  the support polygon. A leg that was there keeps its foot.
- **The gait** (`PlanGait`, again whenever the count of working legs changes): six or more legs, an even count, alternate
  round the body into two groups (a tripod for six); otherwise a wave, one leg at a time.
- **The feet** (`WalkFeet`, server each tick; a client hears them in the poses: "Walker feet on clients"): each foot's next foothold is its
  rest point led by the body's motion (half a gait cycle ahead), dropped onto the ground by a ray from a reach above.
  When no group is in the air, the next group that needs a step (8% of its reach off its foothold, or stretched past
  0.95 of its reach) lifts, but only if the feet left down hold the centre of mass inside them. A foot in the air moves
  on an eased path with a lift of `stepHeight` plus any step up.
- **The body** (`WalkBody`): velocity-level, after gravity and before the contacts. W and S walk, A and D turn, the roll keys
  (Q and E) strafe, Sprint and Crouch raise and lower it; the hips are held at their standing height over their feet; the
  body tilts with half the ground's slope under its planted feet; if the centre of mass leaves the planted feet, it moves
  back over them. Its top speed is `stride x reach / ((groups - 1) x step time)` times the walk speed setting (see "Leg
  speeds"), the step time stretched by load (`mass / load` over 0.5 slows it, past 1.5 it can't stand) and by lost legs.
- **Collapse**: fewer than 3 working legs, more weight than 1.5 times what they carry, or the centre of mass outside every
  working foot: the controller lets go and the body falls as real physics (its hip housings are its contacts). The feet hang
  at their rest pose.
- Nothing allocates per tick; the legs' arrays grow at a rebuild only.

### Drawing (`Source/Render/ConstructRenderLegs.cs`)

Each leg is posed from its hip on the drawn body to its foot: a coxa for an insect, the last segment set by the kind, then
two bones by the law of cosines with the knee towards a pole (forward for mech and digitigrade, up and out for an insect).
Segments taper, knuckles and the pad are drawn in the joint colour, all in one batch. The feet are always the server's: the
host's own, a client's from the poses (protocol 229, "Walker feet on clients"); the renderer never steps them.

### The test walker (`Constructs/Blueprints/TitanWalker.json`, from the same generator)

A bare Capital Chassis Deck 128x64 m on six Titan Mech Legs (three a side at cells 0, 4 and 7), about 29,300 t; a Bridge
Helm on an adapter on the first leg's hip, with a Driving Module wired to that leg (phase 5: the hollow chassis, the helm on
the deck). It walks at 4.5 m/s with its hips
about 28 m up.

What was tried by hand (a throwaway probe, removed: a flat floor, a floor with 3 m bumps under the feet, and a 17 degree
ramp): it stands up from its spawn, walks straight at 4.5 m/s holding height, turns right on D, rises on Sprint, follows
the bumps with 1 to 5 degrees of tilt, climbs the ramp (walking backwards) at 4.5 m/s tilted about 6 degrees, collapses as physics when the three legs of
one side are broken, and its worst tick was under 1 ms on the flat (4 ms on the bumps and the ramp, on a 2-core box
under other agents' builds). Known: raised to the top on the ramp, the uphill legs stretch past their reach (1.08); the
height limit should come from the stretch, not a share of the reach.

Not tried: drawing (no window run), a client over the network, a player riding, real map terrain, the other leg kinds and
tiers on a body, the welder fitting a leg by hand.

## Leg speeds (branch `feat/leg-speed`, protocol 241)

The user (2026-11-11): walker legs walk much faster, a speed setting starts in the middle of its band (0 to 200%), bigger
walkers are faster (longer strides), and walkers stay slower than cars but take worse ground.

- **The data** (`tools/constructs/leg_parts.py`): each tier names the top speed a six-legged electric walker walks at on the
  default setting, and the generator works out its `stepTime` from it; a new leg field `stride` (0.95 for every leg: how far
  the body goes while a foot is planted, a share of the reach; parts without it take 0.5). Top speed = `stride x reach /
  ((groups - 1) x swing)`: a planted foot sweeps its stride while the other groups step, centred on its rest point (the
  foothold leads by half its time down), so a mech leg stays within 0.84 of its reach at full speed and an insect's 0.89.
- **The table** (six legs, unloaded, flat; measured matches the formula within 0.1 m/s):

  | Tier (reach) | step time | electric (swing) | hydraulic | shaft | four legs (trot) |
  | --- | --- | --- | --- | --- | --- |
  | Small (1.6 m) | 0.122 s | 10 m/s (0.15 s) | 12.5 | 15 | the same as six |
  | Medium (6 m) | 0.38 s | 12 (0.48 s) | 15 | 18 | |
  | Large (20 m) | 1.086 s | 14 (1.36 s) | 17.5 | 21 | |
  | Titan (48 m) | 2.28 s | 16 (2.85 s) | 20 | 24 | |

  Hydraulic and shaft keep their strengths (1.0 and 1.2 against electric's 0.8), so their swings are shorter (a Small
  shaft leg's 0.10 s). Load slows them as before: the bare Titan Walker (32,000 t) walks at 15.5, the fitted one at 14.6.
- **The setting** (`walkSpeed`, every leg, "Walk speed", 0 to 200%, default 100; every profile, Simple too): the walker reads
  its lead leg's, else the first leg whose setting was changed (`ConstructPhysics.WalkSpeedOf`, any peer). It scales the
  commanded top speed, not the legs' strength: above half it quickens the steps (the stroke stays the same), below half the
  steps shorten. Saved with blueprints and worlds and sent as any part setting. Walking costs `LegCarryCost` of the weight a
  metre a second as before, so twice the speed draws twice the power, and the plant and the bank cap it (`PowerLift`): a
  walker held below its set speed says so (`Construct.WalkPowerHeld`, a bit in the pose's walk flags, protocol 241). The HUD's
  walk line: "Walk 200%: 31.1 m/s top", orange with ", all the power can feed" when held.
- **Four legs trot** (`Trots`): Auto plans four working legs as two diagonal pairs; a pair lifts with two feet left down
  (the controller holds the body's height and tilt) when the centre of mass is within a quarter reach of the line between
  them. Five legs still wave (a third of a tripod's speed or less).
- **Gait fixes the speed needed**: the support test is the convex hull of the planted feet (`InsideFeet`; at a long stride a
  middle foot passes the rear one beside it and the feet in leg order crossed, so the walker dropped its feet every step);
  the next group lifts on the tick the last one lands, with the overshoot carried (a tick's wait was a tenth of a Small step
  and stretched the stroke past the reach); a turn while walking slows both so the fastest rest point goes no faster than
  the legs walk (an arc stretched a foot to 1.56 of its reach). Stopping is three times quicker than starting (`WalkBrake`).
  **The edge ahead** (`EdgeAhead`, one ray a tick): past the front feet by the stopping distance and a third of a reach, on
  the plane of the planted feet; no footing there (or a drop of more than 0.6 reach) caps the speed so that it stops within a
  quarter of `EdgeLag`, and the feet then stop it at the edge as before. The footfall thump, cargo pop and riders' shake scale
  with the setting (`StepFeel`: 0.6 standing still, 1 at 100%, 1.4 at 200%). The foot push on bodies uses the step as it is.
- **A leg that breaks off** no longer swaps the other feet: `ConstructWorld.Compact` renumbers the legs' parts (before, the
  next `BuildLegs` matched old part numbers to new parts, so a foot 100 m off stayed planted and the walker stood still).

Measured (a throwaway probe, removed; through the real seat path, host alone unless said; steady speed on flat ground):

| | 50% | 100% | 200% | stop from full | other |
| --- | --- | --- | --- | --- | --- |
| Small six-legger (378 kg, Proving Ground drag strip) | 4.98 m/s | 9.96 | 19.91 | 1.6 m from 8.4 m/s, 3.0 m from 16.8 | turn in place 137 deg/s; arc stretch 0.85 |
| Small four-legger (trot) | 4.98 | 9.96 | 19.9 | 1.6 m, 3.0 m | |
| Medium six-legger (14.7 t) | | 12.0 | 24.0 | 1.9 m from 10.2 | |
| Titan Walker (Streamed Terrain) | 7.8 | 15.5 (13.3 over rolling ground) | 31.0 | 4.8 m from 26 | one leg broken off: 3.7 m/s (a wave of five) |
| Titan Walker Fitted (main's) | | 14.6 | 29.2 | 4.6 m from 25 | worst tick 4 ms |

- Cliff: a Small walker at 200% off the end of a frozen 32 m deck 15 m up stopped with its centre 1 m from the edge; a Medium
  at 200% on a 64 m deck 30 m up stopped 4 m from it. Neither fell.
- Power, the bare Titan Walker: walking on the flat costs 142 MW at 15.5 m/s and 284 MW at 31 (plus the climbs: weight x rise).
  Over rolling ground its 30 GJ bank went 0.51 GJ/s at 100% and 0.71 GJ/s at 200%: about a minute and 40 s from full before
  the bank's share (`BankSeconds`) caps the speed. On 5% of its bank it walks 3.4 to 3.6 m/s, held by the power, and the
  plant still charges the bank at that pace: the plant alone walks it at about 3.5 m/s.
- A client over RakNet (60 ms lag, 2% loss): its copy is 2.4 m behind at 10 m/s and 4.8 m at 20 (its drawn delay), the feet
  the same as the body's lag; stopped, body and feet within 1 cm; top speed and the power flag the same.
- Worst ticks: 2 to 4 ms the Small and Medium, 4 to 8 ms the Titans, 11.9 ms once on the bare Titan's first ticks after a leg
  broke off (the rebuild).

Not tried: a Large walker; a ramp (the Proving Ground's hill-climb ramps and the streamed terrain's trees blocked the probe's
straight runs, so hills were only rolling streamed ground); a Titan at a cliff (no deck big enough); a client driving; a
window run (the drawn legs at a 0.08 s swing, the sound at 7 footfalls a second); hydraulic and shaft legs at speed.
