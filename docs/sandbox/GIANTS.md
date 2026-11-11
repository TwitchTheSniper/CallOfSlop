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
  back over them. Its top speed is `0.45 reach / (groups x step time)`, the step time stretched by load (`mass / load`
  over 0.5 slows it, past 1.5 it can't stand) and by lost legs.
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

## Phase 3 (branch `feat/giant-walker-3`)

- **Weld sockets on segments** (`Source/Constructs/ConstructWalkRiders.cs`): every leg part gets a socket a segment, `seg0`
  hip first, appended at load after the file's own (`ConstructParts.WithLegSockets`: socket indices never shift; the leg's own
  size class). They sit on a canonical rest pose: the leg hanging straight down from under its hip housing, outer face +x,
  so each segment's rest frame is square to the part (`LegDef.RestCentre`, `LegDef.RestFrame`). A plain part welded there,
  and plain parts welded only to such parts, is a **rider** (`Construct.RideOf`, `RideSeg`, found by `BuildRiders` at a full
  rebuild, before the collider; a walker never rebuilds by the journal). A rider keeps its place in the rest frame on the
  canonical segment; `ConstructPhysics.RideFrame` carries it to the segment as posed (`t + q x`). It is left out of
  `SolidCount` (so the collider, decks, fused units and the rays' part loop), the merged mesh and the part-by-part draw;
  `DrawLegs` draws it on its segment and `LegRay` hits it (the rider takes the damage). Its mass is in the body's (at its
  canonical place: the centre of mass is a little off) and in its leg's `Carry`: a leg swinging a tenth of its load steps
  half as slow again, at most twice (`CarrySlows`). A part that isn't plain (a gun, a seat, a light) on a segment socket
  stays a body part at the canonical place.
- **The welder on a leg**: an aim at a drawn segment (or a rider) is taken to the rest frame of the nearest segment
  (`RestOfDrawn`), so the ghosts are the segment sockets there; the crosshair pick, the ghost drawing and the server's
  reach (`ConstructEdits.AttachReach`) put a segment socket where the leg is drawn (`RideFrameOf`).
- **Gait profiles** (`Source/Constructs/ConstructWalkProfile.cs`): part settings on every leg (configure tool, from
  `leg_parts.py`; so blueprints, saves and the wire carry them as any setting). `walkProfile`: Simple (default, nothing
  read), Basic, Advanced. The walker follows its lead leg (the highest profile, the first). Basic: `stride` (50-150%: the
  foothold lead, the step trigger and the top speed), `stepLift` (50-250%), `bodyHeight` (60-130% of the standing height,
  still capped by the stretch), `gait` (Auto, Tripod: half the legs from four up, Wave). Advanced, each leg's own:
  `legPace` (its step time), `legLift`, `legSpread` (its rest foot out from the hip, 70-130%), `legGroup` (its tripod half).
  `WalkTune` reads them each tick on the server and in a client's `PoseFeet`; a change re-plans the gait (`WalkGaitKey`).
  No wire change (protocol stays 225).

What was tried by hand (a throwaway probe, removed: an offline Sandbox game on the Construct Yard with the Titan Walker):
a Capital Strut welded through the real Attach request onto the last segment of a leg, aimed from beside the drawn
segment (the ghost found from the drawn point, reach good; from the canonical place it is "too far away"); it became a
rider (`SolidCount` 0, the leg's carry 76.8 t); a ray at the drawn strut hits it, a ray at its canonical place misses,
standing and after the legs moved. Through the real Configure request: a stride of 150% does nothing under Simple, and
under Basic gives 1.5 times the top speed; Wave makes six groups (a third of the speed); Advanced with a leg's group set to
Second moves that leg to the other half. Worst game tick about 10 ms once on the busy 2-core box (5 ms before).

Not tried: the body height setting in a clean scene (the yard spot was uneven: the planted legs read 41 to 54 m hip over
foot for a 48 m reach, worth a look on flat ground); walking with a rider while driven (the probe's throttle didn't take
without a seated driver); a client over the network (its riders come from the links and its gait from the settings, as on
the host); drawing (no window run); a blueprint round trip with a rider; a hydraulic or shaft-fed walker.

## Phase 4 (branch `feat/giant-walker-4`)

- **Body height**: tried on flat ground, it works (Basic profile: hips 28.4 m over the feet at 100%, 19.5 m at 70%, 37.4 m at
  130%). Phase 3's "no drop" on the yard was the ground: feet planted far below the hips (41 to 54 m for a 48 m reach) with the
  body resting on its own contacts, not the setting.
- **Legs use what feeds them** (`LegWork`, `ConstructWalkLegs.cs`): each tick the legs' work is the weight times
  `LegCarryCost` (0.03) of the speed along the ground, plus the climb, plus a holding share (`LegHoldRate`, as if lifting it
  0.01 m/s), shared out by what each leg carries and taken from the feed that powers it (`CLeg.Feed`): an electric leg draws
  the battery (`MotorWork`); an oiled leg asks its pump for flow at the pump's pressure (`CHydPump.LegUsed`, `LegNeed`, added
  in `ConstructHydraulics.Solve`), and a pump asked for more than it gives weakens its legs down to a quarter; a pump on
  power draws the battery for the work, one on a shaft loads the engine through the pump's torque; a shaft-fed leg loads the
  engine at the crank (`Construct.LegLoad`, in `ConstructDrillDrive.Carry` with the drills', rails' and pumps' loads) and an
  engine held down by it stalls like one held by pumps (`StepStall`). A flat battery, a stalled engine or a starved pump stops
  the legs. Server only, no wire change. The Titan Walker carries a Capital Battery Bank (the legs draw 3 MW standing and about
  45 MW walking; its 300 kJ Battery would be flat in a tick). `TitanWalkerFitted.json` still has only the small Battery: it
  needs `--gen-interior` run again from the new `TitanWalker.json`.
- **Ride feel** (`Source/Constructs/ConstructWalkFeel.cs`, `Source/Game/GameWalkerFeel.cs`): when a group of feet comes down
  the server dips the body (`ThumpDip`, 0.35 m/s) and tips it towards those feet (`ThumpTip`), and throws loose bodies resting
  on its deck (under 1% of its mass, over its plan) up and a little sideways (`CargoPop`, 1.6 m/s, a hash for the side, no
  random numbers); with a group in the air the body leans towards its planted feet (`SwayLean`). The body's motion is real, so
  riders feel it through the ride frame and cargo through its contacts. Each peer hears each footfall where it lands
  (`Sfx.LandThud`, louder and deeper for bigger legs) and a player riding the walker (standing on it, seated or driving) gets
  camera shake (`ThumpShake`); one near a foot gets a little.
- **Stabilisers** (`tools/constructs/stabiliser_parts.py`, `Constructs/Parts/Stabilisers`, section Legs): a gyro housing a
  class (Stabiliser 600 kg steadied, Medium 40 t, Large 1,500 t, Capital 6,000 t), a part file's `"stabiliser": {"steady":
  kg}` block. `WalkRough` = 1 minus the kg steadied over the walker's mass, at least `MinRough` (0.12), found at a rebuild from
  working stabilisers; it scales the dip, the tip, the sway, the cargo's throw, the thump's loudness and the shake.

What was tried by hand (a throwaway probe, removed; flat ground, the Titan Walker driven forward 10 s): the body heights above;
electric: 3 MW standing, about 45 MW walking at 3.5 m/s, and with the battery emptied the legs stop and it sits on its hips; a
hydraulic walker (a Capital Hydraulic Pump on power, a hose to each leg, routed over the deck): all six legs on oil at full
strength, 4.5 m/s, the pump asked 1,375 of its 1,400 L/s walking, the battery drawing for it; a shaft-fed leg (one shaft from
a Capital Diesel Engine: an engine's output takes one shaft) runs at 1.2 strength and loads the crank 1.1 MN m at 114 rpm, and
with the engine stalled that leg falls back to the battery. Ride feel, bare / 2 / 5 Capital Stabilisers: roughness 1 / 0.62 /
0.12, worst tilt rate 0.109 / 0.067 / 0.013 rad/s, deepest dip 0.31 / 0.19 / 0.04 m/s, a Buggy on the deck hopped 0.40 /
0.14 / 0.03 m. Worst tick 4 ms (8 ms once, on the busy 2-core box).

Not tried: the sound and the camera shake (no window run, no client); a client over the network (its thumps come from its
own feet); a walker on six shaft-fed legs (it needs a shaft splitter: an engine's output takes one link); a pump on a shaft
feeding legs; an engine stalled by legs alone; real map terrain; seated riders' shake.

## Phase 5 (branch `feat/giant-walker-5`)

- **Centre sockets check**: since phase 3 the "Centre sockets" self-test failed on every leg part ("isn't structural but
  gained N"), because it took every socket added at load for a centre socket. The check now leaves out a leg's segment weld
  sockets (`seg0`.., from `LegDef.SegSocket`) and checks they sit straight after the file's own. The check was changed, not
  the sockets: they are meant to be added at load (from the leg block, so a changed segment list moves them with it), their
  indices come after the file's own as the centre sockets' do, and marking them otherwise would need a new socket field for
  no gain.
- **The Titan Walker** (`tools/constructs/leg_parts.py`, `blueprint()`): its root is the `Capital Hollow Chassis 128x64 m`
  (the interiors track's, `Constructs/Parts/Interior/Capital`): the same footprint, mass (28,160 t), health, armour and 44
  outer sockets as the Capital Chassis Deck, so the leg mounts carry over, with the void storeys inside. Hollow, not the
  Armoured Hull: the armoured one is 11,264 t heavier (39,424 t), which slows the legs further under an interior's mass; an
  armoured walker can be a variant later. The helm's adapter moved from the first leg's hip to the deck's `top0.0` socket.
- **Legs on a flat battery**: an electric leg is fed only while the battery holds a tick of the holding work (`HoldKJ`: the
  weight times `LegHoldRate`, 60 kJ a tick for 37,000 t). A flat battery comes back on at 0.2 kJ, so a genset's trickle (the
  fitted walker's 100 kW Small Genset) switched the legs on every other tick, and the walker walked on it at full speed while
  its draw was clamped to the little charge there was.
- **Walk speed under load**: as intended. With ample power the fitted walker (6,587 parts, 36,557 t, from main's
  `TitanWalkerFitted.json`) stands at 28.4 m on 3.6 MW and walks at 3.35 m/s, against 3.52 m/s bare (31,089 t): the load share
  (mass over what the legs carry: 0.63 against 0.54 at the electric feed's 0.8 strength) slows the pace by 5%. The interiors
  run's 0.09 m/s came from power, not load: the fitted file has no Capital Battery Bank, so its legs flickered on the genset (and
  now it sits down on its hips, "no power to the legs").

What was tried by hand (a throwaway probe, removed: the blueprint built in a world of its own on a flat floor, settled 4 s,
driven 10 s forward and 5 s turning right): bare, it stands with its hips 28.4 m over its feet on 3 MW, walks at 3.52 m/s
(50 MW at most), turns right on D, worst tick 0.8 to 2.8 ms; the fitted walker as above, with the bank's charge given by the
probe; without it, it sits down. Its worst tick was 19 to 30 ms (6,587 parts and 191 joints, on the 2-core box at a load of 2;
the bare walker's under 3 ms): worth measuring alone.

Not tried: a player sitting in the moved helm (the probe drives the seat directly); the interior regenerated on the new
blueprint; drawing; a client.

### For the interiors track

- **The helm**: step 8 (`Bridge Helm`), on step 7 (`Adapter Capital to Small` on the root's `top0.0`), centre about
  (0, 8.6, 0) in the root's frame, facing -x (so the bow stays at the x-min end), on the deck top at the bow beside the first
  leg (-z side). Its Driving Module is step 9, on the helm's -z side (about (0, 8.16, -0.65)), wired from the helm and into
  leg step 1 (`-z0`: its hip housing spans x -8..8, z -24..-8, so the module stays within the 8 m wire of it); step 10 is the
  helm's small Battery, step 11 the Capital Battery Bank (on `top5.1`). Step indices are unchanged from phase 4.
- **Taking the helm over**: a Driving Module takes one input and the walker's helm has it. Build the bridge round this helm
  (its bounds are a keep-out, about 1 m square at x 0, z 0 of the root), or, if the bridge goes elsewhere, the bridge's helm needs a Driving Module
  of its own within 8 m of a leg's hip housing (the legs sit on the deck's long sides at x 0, 64 and 112).
- **The chassis**: `Capital Hollow Chassis 128x64 m` is step 0 now, so `--chassis hollow` is no longer needed.
- **Power**: the legs draw about 3 MW standing (3.6 MW fitted) and 45 to 50 MW walking, and are fed only while the battery
  holds 60 kJ a tick. Keep the Capital Battery Bank (step 11): the interior's batteries and 100 kW genset can't hold it up.
- **Regenerate** `TitanWalkerFitted.json` with `--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1` (left to the
  interiors session: the file is theirs). The fitted body's tick was 19 to 30 ms here; the presses' link scans were the cost ("Fitted walker" below: 1 to 3 ms now).

## Fitted walker (branch `feat/walker-interior`, with the interiors track)

The joint record for the Titan Walker with its interior (`TitanWalkerFitted.json`, `--gen-interior
Constructs/Blueprints/TitanWalker.json --seed 1`); INTERIORS.md points here.

- **Current.** Regenerating on main gives the committed file byte for byte: 6,322 parts, 23,485 links, 6 joints (the lifts),
  38,234 t, the Capital Battery Bank kept, the Bridge Helm (part 4902) is the drive seat. Driven through the seat as
  `GameSandbox` does (the seat's keys with `Occupied`, `Driver`, `DriverSeat`, `ConstructEdits.DriveInput`): it stands at 29.2 m,
  walks at 3.05 m/s on W and turns right on D (5 degrees in 4 s, from a walk).
- **Tick time: the presses.** The worst ticks (19 to 30 ms in phase 5) were not the legs. Every sixth tick
  `ConstructPress.Step` took 3 to 6 ms: the interior's 45 Presses and 45 Die Lathes each looked for their die and pieces with
  `OnSocket`, a scan of all 23,485 links, a dozen times a machine. `OnSocket` and `MachineOf` now walk the part's own links
  (`Construct.FirstLink`/`NextLink`, the index the fuse code already keeps). The press step is 0.05 ms a tick on average now.

  | Fitted walker, 2-core box at load 6 to 7 | before | after |
  | --- | --- | --- |
  | Standing: median, p95, worst (ms) | 0.6, 5.0, 8 to 25 | 0.55, 0.7 to 1.1, 1.7 to 3 |
  | Walking | 0.5 to 0.7, 4.2 to 7.2, 9.1 to 9.4 | 0.55, 0.7 to 0.8, 1.2 to 1.4 |
  | Turning | 0.5 to 0.8, 3.8 to 7.2, 6.5 to 8.9 | 0.5 to 0.6, 0.6 to 1.0, 1.1 to 1.3 |
  | Sat down, no power (hull on the ground) | not measured | 2.5, 4.2, 4.5 |

  A quiet tick's cost is the logic step (0.15 to 0.3 ms over 6,322 parts), the gait (0.2 to 0.3 ms) and the world contacts (0.1
  ms). Single ticks of 9 to 45 ms still show up, once or twice a run, in a different stage each time (the box's other builds
  taking the core; with tiered compilation off none passed 9 ms), plus one 14 ms tick in `ConstructCargo.Step` in three runs of
  four that tiered compilation off also removed (a JIT tier-up). The first ticks after the build (100 to 140 ms, then 30 to 40)
  are the first rebuild and the JIT. Standing and walking allocate nothing in the step; sitting down without power, the doors
  and the power step allocate 7 to 12 KB in 6 s (doors settling as their wired logic loses power).
- **Living inside while it walks** (a throwaway probe: two players on room floors, one on a lift car, a loose Chest body on a
  room floor, the walker driven 6 s):
  - players on the floors stay put: 0.00 m drift in the ride frame, on the deck storeys and in the chassis void;
  - a swing door opened on Use mid-walk and settled open; a lift car's button pressed mid-walk raised the car 3.00 m with its
    rider on it (0.00 m drift on the car's segment);
  - the loose Chest slid 0.51 m across its floor in 6 s and was not thrown up.
  The ride is rougher than the bare walker's (phase 4: worst tilt rate 0.109 rad/s, dip 0.31 m/s): fitted, 0.16 rad/s and 1.1 to
  1.2 m/s, at roughness 1 (no stabilisers).
- **Stabilisers: no room yet.** The fitted walker would need about 38,000 t steadied for the smoothest ride. A Capital Stabiliser
  (6,000 t, 32 x 32 x 18 m) is taller than the chassis void (14 m), and on the deck top each takes 2 storeys of a 32 m square
  from the rooms. Large Stabilisers (1,500 t, 8 x 8 x 4.5 m) fit the void, but the void's three floor sockets are the void
  interior's pads now (an adapter on them left the void storeys with nothing to stand on: 839 refusals). Hung under the belly on
  adapters (23 Large, about 1,800 t) they would steady most of it and leave about 16 m under the hull when standing, but a walker
  that sits down would sit on them. Left for a decision: under the belly, on the deck top (fewer rooms), or a stabiliser that
  is a room's furniture (a small-class part the builder can place).

What was not tried: a client (the walker drawn and its riders' views), a player walking through the rooms while it walks (the
probe's players stand), the seal switches by aim while walking, the production room working while walking (an Assembler fed
from storage), other seeds, real map terrain, more than one fitted walker in a world.

## Walker fixes (branch `feat/walker-fixes`, from a code review)

- **Gravity.** The legs' work and the holding gate (`HoldKJ`, `LegWork`) used 9.81 m/s2; they now use `WalkGravity`: the game's
  `ConstructPhysics.GravityAccel` (18), or the body's own where a round world or a gravity generator applies (`ConstructGravity.GravityFor`,
  as the physics weighs it), less Anti-Grav's share. The height loop also had a lag of g / 6 under it (the velocity it eased towards
  was taken after gravity), so the body stood about 3 m/s short of its aim and, once the rise was capped by power (below), couldn't
  rise at all: the legs now take this tick's gravity back before easing (`WalkBody`).
- **Real power.** The Capital Battery Bank held 10,000 GJ (about 62 hours of walking): now 30 GJ (`big_drive_parts.py`
  `BATTERIES`; the Large Battery Bank 200 GJ to 10 GJ, so it stays below the capital one; no shipped blueprint uses it). The Titan Walker
  gets a plant (`blueprint()` steps 12 to 15): a `Large Fuel Bunker` (1,000,000 l) on an `Adapter Capital to Large` on the deck's
  `top7.3`, with a `Large Genset` (20 MW, 7 l/s at full) on each end of it, touching it so the fuel reaches them: 40 MW. The legs
  walk no faster and rise no faster than the circuit feeds (`PowerLift`: the plants, alternators and the bank's charge spent over
  `BankSeconds` (20 s), times `PowerMargin` (0.8), less the holding work; a rise takes it first, the walk the rest at
  `LegCarryCost` a metre a second). So a running-down bank slows the walk smoothly to what the plant keeps up, and a flat bank on a
  plant no longer flickers the legs on and off. Bare Titan Walker (32,159 t), measured: standing 9 MW of leg work (11.5 MW drawn),
  so it stands indefinitely on the plant; walking 3.3 m/s at 80 to 110 MW drawn (about 60 MW net from the bank: some 8 minutes of
  walking from a full bank, 4 after a stand-up; about 45 minutes standing on the bank alone); on the plant alone it walks at 0.7
  m/s. Climbing is the weight times the rise: standing up from its hips (about 22 m) costs about 15 GJ and takes 25 s at full
  bank, and a 17 degree ramp draws 230 to 470 MW. Fuel: the gensets burn 14 l/s at full (while the bank charges or the legs walk)
  and about 4 l/s standing on a full bank, so the bunker lasts about 20 hours of hard use; refuelling is the limit.
- **Footholds** (`Foothold`): a foothold is ground the ray finds within `FootReach` (0.98) of the leg's reach from its hip and no
  steeper than `FootSteep` (normal 0.55 up, about 57 degrees). Failing the aim it tries a third and two thirds of the way in towards
  the hip and a quarter reach to either side (`FootTries`, 5 rays at most, only when the first fails). A leg with none (`CLeg.Hold`
  false) doesn't step: a planted foot stays down where it is; a foot over nothing (`CLeg.Grounded` false) hangs, reaching down, and
  carries nothing until it finds ground and steps onto it. Only feet on ground count for support (`Supports`, `HeldWithout`). If the
  feet as found don't stand round the centre of mass (none on ground, or it is already over the edge), it collapses that tick and
  falls as physics ("no ground under its feet").
- **The body follows the gait** (`GaitShare`): a planted leg the motion stretches slows the body from `StretchSlow` (0.95 of its
  reach) and stops it at `StretchStop` (0.99); a foot with no foothold ahead lets the body go on only until the foot is `EdgeLag`
  (0.3 reach) behind its rest point, so at a cliff it stops with the front feet at the edge ("no footing ahead: the legs find no
  ground in reach"; backing off works). A group that needs a step but can't lift (`WalkWait`) holds the body to a crawl (15%) while
  it shifts its centre of mass towards the middle of the feet that stay down; a group lifts only with the centre of mass
  `LiftMargin` (4% of reach) inside them.
- **Feet push**: a planted pad only eases a light body it overlaps out (`PadEase`, 0.5 m/s up and away, nothing once clear); a
  stepping pad adds its own motion.

What was tried by hand (a throwaway probe, removed: the blueprint on the flat part of the Proving Ground, the ground for the feet's
rays replaced by a plane, a cliff 70 m ahead, a 17 degree ramp 50 m ahead, or a void 20 m behind the start): it stands up from its hips
in about 25 s and walks at 3.3 m/s; with the bank emptied it walks at 0.7 m/s on the gensets; at the cliff it stops with its front
feet 0.1 m short of the edge and backs off on S; spawned with its front half over the void it doesn't stand ("no ground under its
feet") and lies on its hips; up the ramp at 2.9 m/s; one middle leg wrecked 1.3 m/s, one each side 1.2 m/s, a four-legged walker
(the middle legs taken out, wave gait) 1.2 m/s, none collapsing on the flat; a Chest in a planted pad eased out at 0.5 m/s. Worst
ticks 0.1 to 4 ms walking, once 12 ms at the cliff (the box at a load of 7 to 20 from other builds).

Not tried: a client (its feet run the same foothold search on its own ray; nothing new is on the wire, protocol unchanged); real
map cliffs and pits (the probe's ground was a plane under the rays, the body's contacts the real map); a body that falls into a pit
(the void case lay on the real ground under it); a hydraulic or shaft-fed walker with the new power cap (it applies to electric legs
and pumps on power only); the fitted walker; a round world's gravity; drawing.

### For the interiors track (walker fixes)

- **Moved**: the Capital Battery Bank is step 11 on `top7.0` (the stern, -z corner: x 104 to 120, z -8 to 8 in the root's frame,
  y 8 to 24), off the stair hatch at grid 22,3 and the decks' spine.
- **New**: step 12 `Adapter Capital to Large` on `top7.3` (the stern, +z corner), step 13 `Large Fuel Bunker` on its `front2.2`
  (centre about (112, 14, 48), 12 m cube), steps 14 and 15 `Large Genset` on the bunker's +x and -x faces (centres about (124, 14, 48)
  and (100, 14, 48), 12 m cubes; the +x one hangs 10 m over the stern). Keep the rooms off grid columns 25 to 31 at rows 0 to 3
  and 12 to 15 on the top deck (the 4 m grid from x -8, z -8). Steps 0 to 11 keep their indices.
- **Power**: a fitted walker heavier than the bare one draws more in proportion to its mass; 40 MW stands a walker of about
  100,000 t on the plant alone (the bare one draws 11.5 MW standing).
- `TitanWalkerFitted.json` needs regenerating from the new `TitanWalker.json` (left to the coordinator).

## Walker feet on clients (branch `feat/walker-net`, protocol 229)

Before this a client's renderer ran `WalkFeet` itself on the pose it drew (its own gait clock, group, footholds on a
different ray and swing time), while the server decided leg hits (`LegRay`) and pads (`FeetBlockPlayer`) on its own feet.

- **On the wire** (`Source/Constructs/ConstructNetWalk.cs`): a pose's asleep byte gains bit 1 (`PoseWalker`) on a body with
  legs, and after the rotors comes a walk block: flags (walking, a group in the air, feet put down), the problem as a number
  (`ProblemNotRound` and the other constants in ConstructWalk.cs; "N working legs" carries N), top speed (cm/s) and leg
  power (W), then feet: leg part (u16), flags (swinging, grounded, foothold) and the point in cm from the pose's position
  (9 bytes). A foot goes when it moved (place or flags) in the last `HotWalkTicks` (12) ticks of the walk clock
  (`WalkTicks`, `CLeg.NetMoved`, noted by `NoteFeet` after each gait tick), plus `FeetRefresh` (3) in turn on a cursor
  (`PoseFeetAt`), at most `PoseFeet` (24) a pose; so a planted foot costs nothing after its last few poses, a joiner or a
  lost packet is healed by the turn, and a body with more legs takes turns. A Titan Walker's block: 10 bytes plus 9 a foot
  that moved (3 stepping feet and 3 in turn: 64 bytes).
- **On a client**: each pose's feet are kept with the body's history (`NetHistFeet`, beside the joints' rows) and drawn at the
  body's drawn tick (`SampleFeet`, after `SampleAt`): a line between the two poses either side (a planted foot is the same in
  both, so it stays exactly still), a swinging foot carried on a little past late poses as the body is. `FeetSet` waits
  until every foot has been heard (a leg first heard fills its column of the history). So the drawn feet, the legs' hit
  boxes (`LegRay`), the pads a predicting player stands on and the footfall thumps (`GameWalkerFeel`) are the server's at
  the drawn tick. `WalkProblem`, `WalkTop` and `LegWatts` on a client are the server's too (the HUD). The renderer's
  `PoseFeet` and `WalkStamp` went.

Tried (a throwaway probe, removed: host and client over RakNet in one process, 120 ms lag and 3% loss on the client, the
bare Titan Walker on the Proving Ground, the client's feet at its drawn tick against the server's at that tick): standing,
every foot within 8 mm (the centimetre grid); turning in place, the worst foot of a frame 2 cm off on average, p95 4 cm,
worst 37 cm (late or lost poses while a foot swings at up to 9 m/s); one tick of offset either way doubles the error, so
the ticks line up. The old client feet in the same run: about 1 m off standing, 4 m (median) to 36 m turning. A client
teleported onto a planted pad was never corrected by more than 3 mm. Not tried: walking straight (in the probe the
walker turned on A/D but didn't walk on W, with the throttle set straight on the body: see "Walking on real maps"), a
ramp, shooting a leg as a client, a big world's interest-banded poses (a far walker is sent every 8th pose tick, longer
than `HotWalkTicks`: its feet arrive in turn), a walker with more than 24 legs, a window.

## Walking on real maps (branch `feat/walker-drive`)

The walker-net probe saw the bare Titan Walker turn but not walk on the Proving Ground. A probe through the real chain (the host's
player respawned, seated in the Bridge Helm by `ConstructEdits.Use`, W/S/A/D in `HeldButtons`, the roll keys through
`CActKind.SeatKeys`, so `GameSandbox`'s seat loop, `DriveInput` and the Driving Module wire to the leg) found three things:

- **The probe's driver was gone.** A player who never spawned isn't alive, so the seat loop stood them up on the first tick
  (`SandboxStand`) and the walker had no driver (`Driver` 255): the gait ignores the keys then. Not a game bug; a probe must
  respawn its player before seating them.
- **A fresh walker spent its first 25 s standing up**, from its hull on the ground (the spawn set its lowest point, the hull,
  on the ground), and the rise took all the power the circuit feeds (`PowerLift`), so the walk's share was 0: W did nothing,
  with no HUD reason. Now **a walker is set down standing** (`ConstructBuild.SetDown`): when at least `MinWalkLegs` of its legs
  are fed (from their links, `PowerLegs(.., live: false)`), the legs' resting feet are its lowest point, as a vehicle's wheels'
  bottoms are, so its hull is about 22 m up, it costs no stand-up energy and it walks on its first tick. An unfed walker is set
  down on its hull as before.
- **The hull pinned by what it was set down over.** On the Proving Ground's flattest spot the hull's stern came down over a
  0.6 x 4 m post: the hollow chassis' floor slab is thinner than the post's overlap, so the contact pushed the hull down (normal
  -Y, 1.1 m deep) and the legs could neither lift nor move it. Set down standing, the hull clears anything under 22 m. A walker
  that sits down (no power) on a post is still pinned when it stands again: crushing what is underfoot (next phases) is the answer.
- **The walk's power before the rise** (`WalkBody`): a rise no longer starves the walk. Walking costs `LegCarryCost` of the
  weight a metre a second (0.1 m/s of lift at 3.5 m/s, against 1.6 m/s the bank feeds), so the walk the driver asks for takes
  its share first (at most half the power) and the rise the rest. A walker standing up from its hips with W held drags its hull
  0.1 to 0.4 m/s for about 10 s and walks at full speed once clear, instead of standing still for 25 s.

Measured (a throwaway probe, removed; 6 s a key from a standing start, host alone unless said):

| | W | S | D (turn) | Strafe (roll key) | Raise |
| --- | --- | --- | --- | --- | --- |
| Proving Ground, bare | 3.20 m/s | 2.37 m/s (brakes first) | 7.5 degrees | 2.0 m/s | +6.5 m |
| Streamed Terrain, bare | 3.19 | 2.35 | 7.5 | 2.0 | +7.5 |
| Proving Ground, one leg wrecked | 1.20 | 0.89 | 2.8 | 0.75 | |
| Proving Ground, two legs wrecked | 1.39 | 1.02 | 3.2 | 0.85 | |
| Proving Ground, `TitanWalkerFitted.json` (main's) | 3.03 | 2.24 | 7.3 | 1.9 | +5.2 |
| Fitted, two legs wrecked | 1.25 | 0.92 | 3.0 | 0.75 | +6.2 |

Top speed 3.5 m/s bare, 3.31 fitted (as before). With a client over RakNet (60 ms lag, 2% loss) the client's copy follows: 0.3
to 1.1 m behind the host's pose while walking (its drawn delay), problem and top speed the same, feet set. The Construct Yard
(64 m square) is too small for a 128 m walker: it falls off the edge, not tried further. Worst game ticks 5 to 9 ms bare on the
loaded 2-core box (load 20 to 40 from other builds), 150 to 225 ms on the fitted walker's first ticks (its first rebuild and JIT).

Found on the way, not fixed (other tracks' files):
- `--gen-interior Constructs/Blueprints/TitanWalker.json --seed 1` on main now writes a fitted walker with **no drive seat**: its
  Bridge Helm went to the deck's `t15_7` and its Driving Module has no wire to a leg (the committed file has `[4903, 1]`). The
  interiors track's builder should keep the helm's wire to a leg (or give the bridge's module one within 8 m of a hip).
- A standing walker falls asleep when nothing moves, and an asleep walker runs no gait, so emptying its bank and bunker while it
  sleeps doesn't sit it down until something wakes it; awake with charge and fuel at 0 it still stood (power 0, `WalkProblem`
  empty). Worth a look with the power phase.

Not tried: a client driving (only the host drove; the client watched), strafing and raising as a client, other leg kinds,
a walker that falls into a pit, drawing (no window run).

### Next phases

1. Terrain: crushing trees, cars, posts and small structures underfoot and under a hull that sits down (the pads' push is the
   start; `Map.BreakProp` through `Game.ServerBreakProp` exists); wading; the obstacle step-over measured on real maps.
2. Autopilot to a destination.
3. Crew stations, the hangar and production line on a walker: the fitted walker runs ("Fitted walker"); next, its stabilisers
   (a decision), the production room working on the move, and a client.
4. 500 m and more: the part reach cap (200 m), the broadphase sphere (a leg's reach now widens it), instanced leg drawing,
   legs of several parts.
5. Power, rest: a walker that keeps standing with no driver on a shaft (the engine stops when nobody drives); a valve's
   signal as a throttle; shaft splitters for six shaft-fed legs.
8. Walker fixes, rest: biped gaits and a trot for four legs (a dynamic balance the static support polygon can't give:
   the capture point, wider feet and a lower stance at rest, a fall past recovery; for the mech suit); crushing what is underfoot; the power cap for shaft-fed legs and pumps
   on a shaft (the engine's torque); a walker that falls into a pit tried on a real map.
6. Riders: a rider's mass at its posed place (the centre of mass), players blocked by riders, non-plain parts riding
   (lights, guns: their systems would need the posed frame); tunable segment lengths and joint count as part settings.
7. Ride feel, rest: stabilisers with power (a gyro that draws charge), a thump that dents soft ground, a client and a
   window check of the sound and shake.
