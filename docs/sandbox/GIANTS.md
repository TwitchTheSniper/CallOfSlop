# Giants: walkers and moving bases

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
  underfoot. Ride feel from parts (thumps, shake; stabilisers) later. Size target 500 m and more; damage per part as today.

## Phase 1 (branch `feat/giant-walker`)

### What limits a 100 m construct (the survey)

What was read: `docs/sandbox/CONSTRUCTS.md` (what a construct is, rebuilds, physics, seats, size classes, limits),
`docs/perf/LIMITS.md`, `Source/Constructs` (Construct, ConstructPhysics, ConstructDeck, ConstructDrive, ConstructModules,
ConstructBroadphase, ConstructBuild), `Source/Render/ConstructRender*.cs`.

| Area | What there is | At 100 m | At 500 m |
| --- | --- | --- | --- |
| Float precision | World positions are `float` (`Construct.Pos`, parts on a 1 cm grid in the body frame); space has its own doubles (`SpacePos`) | Fine: a float near 1 km is good to 0.06 mm | Fine within a few km of the origin; past ~8 km a millimetre goes, which the map sizes don't reach |
| One part's size | `ConstructParts.MaxPartReach` 200 m from its origin, 256 boxes, 255 sockets, mass 50,000 t, health u24 | A titan leg reaches 48 m: fine | A leg longer than 200 m needs the reach cap raised, or legs built of several parts |
| Parts a body | 16,000 (`Construct.MaxParts`); big bodies rebuild by the journal (plain parts) | A walker of a few thousand parts is fine | A town on a walker reaches it; fused units ([Fusing](CONSTRUCTS.md#fusing)) are the answer there |
| Joints | 255 a body (a segment is a byte) | Legs are one part each with their own pose, so they spend no joint | The same: the leg rig is not built of joints, by design |
| Collider and solver | Every solid box in a tree per body; sequential impulses; a speed cap of 60 m/s; the broadphase grid is 8 m cells with a 1.2 m margin | A 128 m chassis is one body with a few boxes a part: cheap | A body's sphere (`Radius`) wakes everything in it: a 500 m walker keeps a town of neighbours awake. Wants a box broadphase test, not the sphere |
| Wire reach | 8 m centre to centre (`ConstructBuild.WireReach`) | A capital part's centre is 8 m inside its own cell, so nothing could be wired to it. Fixed: a part bigger than the reach is wired to the nearest point of its bounds (`WireSpan`) | The same rule holds |
| Riders and the ride frame | Riders stand on the body (`ConstructRideMove.cs`), seats pin sitters; the contraption space measures faces | Works as for any big body (carriers, decks) | The ride feel (thumps, shake) is a later phase |
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
- **The feet** (`WalkFeet`, server each tick; a client's renderer on the pose it draws): each foot's next foothold is its
  rest point led by the body's motion (half a gait cycle ahead), dropped onto the ground by a ray from a reach above.
  When no group is in the air, the next group that needs a step (8% of its reach off its foothold, or stretched past
  0.95 of its reach) lifts, but only if the feet left down hold the centre of mass inside them. A foot in the air moves
  on an eased path with a lift of `stepHeight` plus any step up.
- **The body** (`WalkBody`): velocity-level, after gravity and before the contacts. W and S walk, A and D turn, the roll keys
  (Q and E) strafe, Sprint and Crouch raise and lower it; the hips are held at their standing height over their feet; the
  body tilts with half the ground's slope under its planted feet; if the centre of mass leaves the planted feet, it moves
  back over them. Its top speed is `0.45 reach / (groups x step time)`, the step time stretched by load (`mass / load`
  over 0.5 slows it, past 1.5 it can't stand) and by lost legs.
- **Collapse**: fewer than 3 working legs, more weight than 1.5 times what they carry, or the centre of mass outside every
  working foot: the controller lets go and the body falls as real physics (its hip housings are its contacts). The feet hang
  at their rest pose.
- Nothing allocates per tick; the legs' arrays grow at a rebuild only.

### Drawing (`Source/Render/ConstructRenderLegs.cs`)

Each leg is posed from its hip on the drawn body to its foot: a coxa for an insect, the last segment set by the kind, then
two bones by the law of cosines with the knee towards a pole (forward for mech and digitigrade, up and out for an insect).
Segments taper, knuckles and the pad are drawn in the joint colour, all in one batch. On the host the feet are the
server's; on a client (`WalkStamp` older than a quarter second) the renderer runs `WalkFeet` itself on the drawn pose, so
nothing about the feet is on the wire (no protocol change).

### The test walker (`Constructs/Blueprints/TitanWalker.json`, from the same generator)

A bare Capital Chassis Deck 128x64 m on six Titan Mech Legs (three a side at cells 0, 4 and 7), about 29,300 t; a Bridge
Helm on an adapter on the first leg's hip, with a Driving Module wired to that leg. It walks at 4.5 m/s with its hips
about 28 m up.

What was tried by hand (a throwaway probe, removed: a flat floor, a floor with 3 m bumps under the feet, and a 17 degree
ramp): it stands up from its spawn, walks straight at 4.5 m/s holding height, turns right on D, rises on Sprint, follows
the bumps with 1 to 5 degrees of tilt, climbs the ramp (walking backwards) at 4.5 m/s tilted about 6 degrees, collapses as physics when the three legs of
one side are broken, and its worst tick was under 1 ms on the flat (4 ms on the bumps and the ramp, on a 2-core box
under other agents' builds). Known: raised to the top on the ramp, the uphill legs stretch past their reach (1.08); the
height limit should come from the stretch, not a share of the reach.

Not tried: drawing (no window run), a client over the network, a player riding, real map terrain, the other leg kinds and
tiers on a body, the welder fitting a leg by hand.

## Phase 2 (branch `feat/giant-walker-2`)

- **Height by stretch** (`WalkBody`): the body rises no higher than lets every planted leg reach its foot within
  `MaxStretch` (0.94) of its reach, and the driver's raise stops there. On the 17 degree ramp, raised to the top and walking
  up, the worst planted stretch is now 0.93 (it was 1.08).
- **The leg's shape in the world** (`Source/Constructs/ConstructWalkLegs.cs`): the IK the renderer draws by (`LegPose`,
  `Pose`) is shared with the server. `LegRay` meets each segment as the box drawn there (along the bone, tapering) and the
  foot pad; `ConstructWorld.Raycast` reports the leg part, so rounds, blasts' rays and the look hit the segment that is
  drawn, and the leg part takes the damage. A damaged leg carries less and steps slower (`LegSound`: down to a quarter just
  before it breaks), and a broken one is out of the gait as before. The body's `Radius` (so the broadphase and the ray's
  sphere test) reaches a leg's whole length from its hip (`Construct.JointReach`).
- **Feet touch things**: `FeetBlockPlayer` (from `BlockPlayer`) stands a player on a foot pad met from above (as ground,
  not the ride frame: a planted foot doesn't move with the body) and pushes them out of its side; `FeetPushBodies` (server,
  each tick) pushes a loose body that a working pad overlaps up and away, moving with a stepping foot, if it is under a
  quarter of the walker's mass (heavier ones meet the walker's own contacts only).
- **Power** (`LegPower`, `PowerLegs`): a leg works only when fed: a hose into its "Oil in" port from a pump, or from a valve
  on a pump (the pump pumping); the electric circuit (a working battery's links reach it, through the hip and the deck); or
  a drive shaft into its "Drive in" port that the engine turns (`ConstructLinear.ShaftOf`, Turning). Its strength is the best
  feed's share from the part's `power` block (hydraulic 1.0, electric 0.8, shaft 1.2 times the engine's share), and it scales
  what the leg carries and how fast it steps. An unfed (or broken) leg is locked: out of the gait, carrying nothing, hanging
  at its rest pose sagged a little towards its hip. No fed legs, or fewer than three: it collapses as physics on its hips.
  A client works out the feed from the links alone (no pump flow or shaft rpm on its side).
- **Checks and HUD**: a body with legs and no engine no longer says "no engine" (`DriveProblem` is empty); the blueprint
  check says "walks on 6 legs (6 with power)" or "doesn't walk: ..." (`ConstructDrive.Describe`), and the drive seat's HUD
  line is the walker's keys or "Won't walk: ..." (`ConstructPhysics.WalkerProblem`).
- **Data**: leg parts gain a `drive` socket on the hip housing's -z face, an "Oil in" pipe port on its +z face and a
  "Drive in" shaft port (`leg_parts.py`); a leg with `power.shaft` is a `ShaftIn` part. The Titan Walker carries a Battery
  on its Driving Module, so it walks on electric power (3.6 m/s; 4.5 at full strength). Protocol 225.

What was tried by hand (a throwaway probe, removed): the ramp raise above; six rays at the middle of each leg's second
segment hit their own leg, rays 6 m beside the shins miss; leg 0 at 30% health cut the top speed from 4.5 to 3.8 m/s; a
player walked into a pad's side is pushed out to its edge plus their radius, one coming down on it stands on its top; a
38 kg chest in a planted pad is pushed out (2 m in a second); wrecking the battery stops the legs ("no power to the
legs") and the body sits down on its hips; the part check's walker line; worst tick 0.7 ms on the flat after warm-up
(18 ms once on a busy 2-core box during the hit probe, not repeated).

Not tried: a hydraulic or shaft-fed leg (only the electric feed was probed); drawing (no window run); a client over the
network (its feet and feed are its own); a player riding while shot at; the welder fitting a leg by hand.

### Next phases

1. Weld sockets on each segment: a part welded to a segment rides that segment's pose (drawn, hit and carried with it,
   out of the body's collider) and adds its mass to the leg. The plan: generator sockets `seg0`.. at each segment's middle
   on its outer face in a canonical rest pose (part frame: out along +x, knee towards -z), the rest pose taken from the
   part's own +x; riders found from the links at the rebuild, kept as a transform in their segment's rest frame; their
   boxes left out of `SolidCount`, the collider and the merged mesh, and drawn and ray-tested by the leg code.
2. Legs use what feeds them: draw on the battery, take load off the engine's shaft, flow from the pump; a valve's signal
   as a throttle.
3. The Basic profile (stride, step height, body height, gait override) and the Advanced profile (per-leg tuning), on the
   configure tool; tunable segment lengths and joint count as part settings.
4. Terrain: crushing trees, cars and small structures underfoot (the pads' push is the start); wading; the obstacle
   step-over measured on real maps.
5. Ride feel: thumps, shake and tilt for riders and loose parts; stabiliser parts that reduce it.
6. Autopilot to a destination.
7. Crew stations, the hangar and production line on a walker.
8. 500 m and more: the part reach cap (200 m), the broadphase sphere (a leg's reach now widens it), instanced leg drawing,
   legs of several parts.
