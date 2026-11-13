# Arms and cranes: machines that pick up and place pieces (design)

Design only (2026-11-12). No code changed, nothing built, nothing measured: every number below is a proposal or is quoted
from the docs. Written for an Opus agent to build in six phases of 60 to 90 minutes.

The two jobs (the queue item): **speed up Building Station builds**, and **move pieces too large for a conveyor** between
machines, stores and the station.

Sources read: `docs/sandbox/constructs/production.md` (Machine tiers, Build stations, Conveyors, Smart logistics,
Reservations, Building station), `joints-and-logic.md` (Actuator sizes and strength, Linear joints, Pieces of a mechanism),
`big-systems.md` (Large systems), `look.md` (Machine look), `docs/sandbox/design/press-rework.md` and `smart-logistics.md`,
and the code named in section 1 (`ConstructStation.cs` read in part; the rest by their headers and entry points).

Words used here: a **lifter** is the code's name for either machine (`ConstructLifters.cs`); the parts are the **Loader
Arm** and the **Bridge Crane** ("gantry" is taken: the Build Gantry is the camera station on U). A **point** is a place a
lifter picks from or puts to.

## 1. What exists today

| Thing | How it works now | Files and symbols |
| --- | --- | --- |
| Building Station | Fits one part every 2 s plus 0.02 s a kg, 10 s at most (`FitSeconds`, `FitSecondsPerKg`, `FitSecondsMax`, `FitTicksFor`): one timer a job (`StationJob.FitTicks`). `StepJob` asks `NextStep` for the first unfitted step whose parent is in place (`Ready`) and whose kind the hold has, takes it (`ConstructWorld.StationTake`) and fits it (`FitOne`, through `ConstructBuild.FitStep`). The body is frozen and `HeldBy` the station. Its model has "a mast with a boom that points at the bay": drawn, not working | `ConstructStation.cs`, `UI/UiStation.cs`, `RouteRender.DrawBays` |
| What the station can't take | Its hold is a Piece Rack's, 4.8 m long. "A part too big for the hold" is on its not-yet list; the only way in for one is `TakeDropped`: a loose one-part body lying in the bay, put there with the physics gun | `ConstructStation.TakeDropped`, production.md "Not in phase 1" |
| Conveyors | The large-piece link, `RouteType.Conveyor`. A piece is a `CRider` (def, link, front, health, paint, `Dest`, `Tag`, `Flags`), not a part of any construct; 0.5 m/s; a 10 Hz step; clients step riders on themselves between the server's words. Junctions and Routers pass 1 x 1 m, the Large ones 2 x 2 m; the Long Piece Rack holds 2 x 2 m by 12 m. production.md: "cranes, carts on tracks and arms are to come on the same link" | `ConstructConveyor.cs` (`PutMade`, `HandOff`, `Takes`, `Deliver`, `RackRefusal`, `ClientStep`), `ConstructRackPack.cs` |
| Pieces larger than that | A Large press takes blanks to 7.2 m across, a Capital one to 28.8 m; hull panels and vehicle sections pass 2 m. With no conveyor that takes it, a made piece "drops at the front as a body" at the machine's `outAt` | `ConstructPress.cs`, `ConstructGive.DropLoose` |
| Smart logistics | One network a construct: `LogiMap` (nodes, Routers, destinations, next-hop columns) built when links change, never per tick. A piece's destination is a store handle; `Assign` picks a want, else storage; `Pass` runs once per lead piece at a Router. Chains and fetches ride the same conveyors (`ConstructCrafting.RouteTo`) | `ConstructLogistics.cs`, `ConstructLogisticsPlan.cs`, `ConstructReserve.cs` |
| Moving a piece by hand | The physics gun: 5,000 kg in survival, 80 t with tool upgrades. F on a rack's stack; a physics-only piece is never carried in the hands | `ConstructGrab.cs`, `ConstructEdits` |
| Joints and motors | Pistons, motor hinges, turntables and linear rails with ratings (`ConstructPhysics.MotorPrep`), 16 joints a construct chain. A 10-joint arm costs about 0.06 ms a tick on the cloud box. Rails stack end to end into one joint (`Construct.BuildRails`) | `ConstructPhysics.cs`, `ConstructLinear.cs`, `ConstructHydraulics.cs`, `Constructs/Parts/Actuators`, `Linear` |
| Physical crane parts | Crane Boom (8, 32, 64 m: a lattice beam, no behaviour), Winch (30, 300, 3,000 t), Lifting Eye, Magnetic Lock, the Harbour Crane blueprint, the Sky Crane's cargo winch (`IsCargoWinch`). All worked by a player through levers and seats: nothing drives them from an order | `Constructs/Parts/Systems/Moving`, `PartKit/Generators/LargeSystems.cs`, `ConstructLinks.cs` (winch), `ConstructConnectors.cs` |
| A machine that moves material physically | Scoops: the bucket is real parts on real joints, and only the ground's hold is a capped row. Server only, costs a solve while it works | `ConstructScoops.cs` |

So the gap: nothing automatic carries a piece that is wider than 2 m or has no conveyor to ride, and the station has one
pair of hands however rich the base.

## 2. Three approaches

| | A. Scripted lifter | B. Physical jointed arm | C. Bridge crane on rails |
| --- | --- | --- | --- |
| What it is | One machine part. The piece in its hand is a record like a `CRider`, moved along a computed path; the arm's segments are drawn from that path. No joints, no solver, no contacts | An arm the player (or a blueprint) builds from turntables, hinges, pistons and a Magnetic Lock, plus a new controller part that drives the joints' references by inverse kinematics and grips a real body | A bridge that runs on two rail runs over a box of floor, with a trolley and a hook. Motion is three straight axes |
| Tick cost | Idle: nothing (an open list, as `StationOpen`). Moving: a few float operations a lifter at 10 Hz. A decision: one scan of at most 16 points | About 0.06 ms a tick per 10-joint arm while awake (the docs' figure), plus the carried body's contacts and 6 extra solver passes for a jointed construct; an arm holding a load at its rating never sleeps | The same as A when scripted (it is A with a box for a reach); the same as B if built on real Linear rails |
| Network | One event a carry (about 40 bytes): from, to, duration, the piece's key. Clients move it themselves | Every joint's pose (a short each) and the held body's pose at the pose rate, to every client, for as long as it moves | As A |
| Fails how | It can look wrong (a piece sweeping through a wall); it cannot drop, jam or explode | Dropped pieces, overload sag, a piece wedged against the build, grip lost on a lag spike; every failure needs a recovery rule | As A |
| Build effort | 4 phases for the arm, station and network; small and certain | High and uncertain: the controller, IK with joint limits, a path clear of the build, grip, recovery, tuning per size. At least 8 phases, and the station's fits would wait on physics | 1 more phase on top of A (the rail walk follows `BuildRails`) |
| What the player gets | A machine that always works and can be read at a glance | A contraption that is theirs, and its failures | The big-piece mover that looks right in a hall |

**Recommendation: A and C as one system.** One scripted core (`ConstructLifters.cs`) with two reach shapes: the **Loader
Arm** (one part, a ring round its base: the station's fitter and a machine tender) and the **Bridge Crane** (a bridge on
rails, a box: the mover of large pieces). They share the job, the wire, the save and the logistics side; only the reach
test and the drawing differ.

Why not B: the 8 ms tick and "nothing allocates" are met by A at any count, while B spends solver time on every arm and
makes a factory's throughput depend on contact tuning. B already exists for players who want it (the Harbour Crane, the
Sky Crane, levers and a Magnetic Lock) and stays: nothing here removes it. A controller part that drives a hand-built arm
from the same work list is a possible later project (question 1).

## 3. The lifter

### 3.1 Data

A lifter is a storage part (so it has a store handle, which is what a rider's `Dest` and a reservation name, and the
machines' power and `Blocked` bytes) with a `storage.lifter` block. Written by a new part kit generator
(`Source/PartKit/Generators/LifterParts.cs`, `--gen-parts lifter_parts`), never by hand; tiers through `MachineTiers.cs`.

```json
"storage": {
  "lifter": {
    "kind": "arm",
    "base": [0, 1.2, 0],
    "reach": [1.0, 4.0], "up": 3.0, "down": 1.0,
    "payload": 500,
    "piece": [1.0, 4.0],
    "speed": 3.0, "handle": 0.8,
    "power": 600
  }
}
```

| Key | Meaning |
| --- | --- |
| `kind` | `arm` (reach is a ring round `base`, between the two radii, from `down` below the base to `up` above it) or `bridge` (reach is the box under its rails, section 3.4) |
| `payload` | kg: the heaviest piece it carries |
| `piece` | the largest piece: its larger side across, its length (m). Longest side against length, as the station's fit compares sides |
| `speed` | m/s at the hand. `handle`: seconds for a pick and a place together |
| `power` | W while it moves (nothing idle, as the station) |

Segment boxes carry a tag (`"arm": 1..4`, with a pivot), as a Router's `"way"` and `"fence"` boxes do: the renderer poses
them; they are not solid. The base's boxes are the collider.

### 3.2 The Loader Arm's tiers (proposed)

One part a tier, on the machine tiers' ladder and look (`TierStyle.cs`):

| Tier | Reach | Up | Payload | Largest piece (across, long) | Hand speed | Handle | Power |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 Scrap | 1 to 4 m | 3 m | 500 kg | 1.0, 4 m | 3 m/s | 0.8 s | 600 W |
| 2 Workshop | 1.2 to 5 m | 4 m | 1 t | 1.25, 5 m | 4.5 m/s | 0.6 s | 1.1 kW |
| 3 Industrial | 1.5 to 6 m | 5 m | 2 t | 1.5, 6 m | 6 m/s | 0.45 s | 1.9 kW |
| 4 Military | 2 to 8 m | 6 m | 4 t | 2.0, 8 m | 9 m/s | 0.3 s | 3.4 kW |
| 5 Plasma | 2.5 to 10 m | 8 m | 8 t | 2.5, 10 m | 12 m/s | 0.2 s | 5.8 kW |

Reach and piece size follow the tiers' size (x1 to x2.5), payload doubles a tier, speed runs x1 to x4 (not the machines'
x16: a hand has to be watched).

**What that does to a build.** A cycle is `handle + (out + back) / speed`. A tier 1 arm 4 m from the hold: 0.8 + 8 / 3 =
3.5 s a part, with no charge for mass up to its payload; the station alone takes 2 s for a light part and 10 s from 400 kg
up. So one Scrap arm adds about 60 % on light parts and triples heavy ones; a Plasma arm at the same spot fits a part
every 0.9 s. Rates add: the station keeps fitting on its own timer, and each arm is one more pair of hands, at most 8 a
station (`MaxFitters`).

### 3.3 The move

```
struct CLift            // one a lifter part, on Construct.Lifters; rebuilt with the construct
  ushort Part; byte State (Idle, Carry, Return, Stuck); byte Why;
  ushort Def; byte Health; uint Paint;          // the piece in its hand (Def 0: none)
  ushort Dest, Tag; byte Flags;                 // as a CRider's: where the piece is going, for which order
  short FromPoint, ToPoint; int Step;           // points (3.5); a station's step when it is a fit (-1: a carry)
  Vector3 From, To; Quaternion FromRot, ToRot;  // the construct's frame
  float T, Dur;                                 // seconds into the move, and its length
```

- **Path.** Closed form from `From`, `To` and `T`, the same on every peer. An arm: straight up to its carry height, round
  the base in angle and radius (so the piece goes round the mast, never through it), straight down. A bridge: hoist,
  travel and traverse together, lower. The turn goes from `FromRot` to `ToRot` over the middle leg. `Dur` is the path's
  length over `speed` plus `handle`; `Return` is the same path back with an empty hand.
- **Step.** In the conveyor's 10 Hz step (`ConstructConveyor.Every`): `T += 0.1`; at `T >= Dur` the place happens (a fit, a
  put) and the lifter starts back. A decision is made only when a lifter is idle and something in reach changed (a store's
  version, a rider reaching a pick point, a station's ready count), else every 2 s, as a Router's stuck look.
- **Nothing is solid.** The piece in the hand passes through players and bodies, as a riding piece does. It cannot be shot
  off; F on it takes it out of the hand (the rider's rule and request).
- **Stuck.** A lifter holding a piece nothing takes waits with it in the hand: state Stuck, its lamp red, **Alarm** 1, and
  the reason on its look line. Nothing is dropped (the Routers' rule, Q7 of smart logistics).
- **A lifter that goes** (removed, wrecked, its construct deleted) drops its piece as a loose body where it was
  (`ConstructGive.DropLoose`), as a cut conveyor does. Without power it stops where it is and goes on when power returns.

### 3.4 The Bridge Crane

Three parts by class (the classes and loads of the existing Winch and Crane Boom):

| Class | Bridge span | Rail section | Hook drop | Payload | Largest piece | Travel, hoist |
| --- | --- | --- | --- | --- | --- | --- |
| Medium | 8 m | 4 m | 8 m | 30 t | 7.2, 16 m | 2 m/s, 1 m/s |
| Large | 32 m | 16 m | 24 m | 300 t | 28.8, 64 m | 1.5 m/s, 0.75 m/s |
| Capital | 64 m | 32 m | 48 m | 3,000 t | 28.8, 128 m | 1 m/s, 0.5 m/s |

- **What is built.** **Crane Rail** sections joined end to end by `end-` and `end+` sockets make a run, as Linear rails do
  (in line within 2 cm and 3 degrees, at most 64 sections); two runs a span apart, on whatever the player stands them on
  (columns, walls, a hull's ribs). The **Crane Bridge** (the lifter part, `kind: bridge`) goes on a section of one run by
  its socket; its far end must find the other run within 10 cm or it says "its far end has no rail". The walk runs on a
  rebuild only and is neither saved nor sent (`Construct.BuildRails` is the pattern).
- **Reach** is the box: span by the shorter run's length by the hook drop, under the rails.
- The bridge, trolley and hook are tagged boxes posed by the renderer; a cable is a drawn line. Rails are solid.
- No tiers at first: a crane grows by class and by rail (question 7).

### 3.5 Points

A lifter works between **points**: places on its own construct, inside its reach, where a piece can be picked or put.
They are found when the construct is rebuilt (`Construct.RebuildStamp`), into a fixed table (`LiftMap`, at most 16 a
lifter, 256 a construct), never per tick.

| Point | Pick | Put | Through |
| --- | --- | --- | --- |
| A machine's **Piece out** with no conveyor on it (Extruder, Formers, Press, Assembler) | yes: instead of dropping the piece as a body, the machine holds it at `outAt` until a lifter takes it | | `ConstructConveyor.PutMade` gains the lifter's offer |
| A machine's **Piece in** with no conveyor on it (a press bay, an Assembler, a Panel Former) | | yes, by the rules a conveyor's delivery meets | `ConstructPress.TakeFromConveyor`, `WantsPart` / `FeedPart` |
| A Piece Rack, a Lay-down Pad, a terminal's tray, a station's hold | yes (a fetch) | yes | `ConstructCrafting.TakeOne`, `ConstructWorld.RackPut`, `RackRefusal` |
| A conveyor that ends at a **Lay-down Pad** | its lead piece | its start | the pad is the node; the conveyor code is untouched |
| A Building Station's bay | | a fit, straight onto the build | section 4 |

**The Lay-down Pad** (new, data only: a Piece Rack by its `storage.pieces` block, with an open hold and no bars; the part
kit's `piece_rack_parts`): Medium 4 x 8 m and Large 8 x 16 m of floor, stacked 2 m and 4 m high, packed by
`ConstructRackPack` as a rack is. It is where a 6 m plate waits, and the yard a player sets pieces down in: a loose
one-part body resting in its box is taken into its hold at the pad's 2 Hz look (the station's `TakeDropped`, made shared).
A lifter never picks a loose body off the ground itself: it only knows points.

A point out of reach, too heavy or too big for the lifter is listed with its reason and shown red in the reach view
("the Press's bay 1: 6.4 m away, this Arm reaches 5 m").

## 4. The station's fitters

- **Where a step goes.** Costing already builds the blueprint in a scratch world. It also keeps each step's part centre
  and turn in the body's frame (`StationJob.StepAt`, `StepRot`: 28 bytes a step, made again by the costing after a load).
- **Claims.** `StationJob.Claim` (one byte a step: the fitter's slot plus one). An idle lifter whose reach touches the bay
  asks its station (`ConstructStation.Offer`): the first unfitted, unclaimed step whose parent is in place (`Ready`), whose
  kind the hold has, whose place is inside the lifter's reach and whose part is within its payload and piece size. The
  part leaves the hold then (`StationTake`), so the hold's count and the orders stay right.
- **The fit** is `FitOne`, unchanged, called when the lifter's move ends. Between claim and fit the step is neither in
  the hold nor on the body: `Orders` counts claimed steps as held.
- **One fit a tick.** `FitOne` rebuilds the body (the docs' longest single step was 4.7 ms on a 212-step body). A world
  fits at most one part a tick (`ConstructWorld.FitTick`); a lifter that arrives on a taken tick fits on the next. That is
  the rule that keeps eight Plasma arms inside the 8 ms tick.
- **When it goes wrong.** Cancel, Release early, a failed job or a lost power feed while a piece is in the air: the piece
  goes to `StationJob.Return` (given back as the rest is). A step the build refuses at the fit fails the job as today.
- **The station's own boom** stays the first fitter, on its own timer and its 2 s plus mass. Nothing a player has changes
  until they add an arm.
- **Large parts** (phase 4): a part the hold can't take is asked for as today, rides or is carried to a Lay-down Pad in a
  fitter's reach, and is fitted from the pad. The pad counts as hold for that job (`ConstructCrafting.Holds` over the hold
  and the pads its fitters reach), and the panel's bill says "on the Lay-down Pad" where it says "in the hold".
- **The panel** gains one line: "Fitters: the station, 2 Loader Arms (Workshop, Scrap) - about 1.4 parts a second", and
  per kind "needs a lifter: 2.4 t, the Arm in reach carries 1 t" where no fitter in reach can carry it and the station
  itself must.

## 5. How a lifter gets its work

A lifter has no queue of its own and makes nothing, so it takes no work orders. Its work comes from what is in its reach,
in this order, each time it is idle:

1. **A station's step** it can fit (section 4), oldest build first.
2. **A wanted piece**: a piece at a pick point whose destination (`Dest`, set by `ConstructLogistics.Assign` or by a
   terminal's fetch or a chain entry) is a put point in reach, or is reached through one (phase 4).
3. **A piece with no destination** at a pick point: `Assign`'s own rule, "wanted, else storage", over the put points in
   reach. So a lifter with one press on one side and one pad on the other needs no setting at all.

| Phase | The rule in force |
| --- | --- |
| 1, 2 | 1 only |
| 3 | 1 and 3, with the lifter's own points only: a put point that wants the kind (a press bay's blank, an Assembler's input, a station's lack), else a rack or pad with room |
| 4 | all three, on the network: a lifter is a node of `LogiMap` and a way may pass through it. `RouteTo`, the next-hop columns and `Pass` see "lifter L from point P to point Q" as an edge whose cost is its metres plus 2 m a handling, so Routers send pieces toward a lifter's pick point, fetches and chains cross it, and overflow reaches a pad no conveyor serves |

**Settings** (configure tool, group Lifter; `CfgKey` at the end): `liftMode` (Auto, Station only, Carry only, Off),
`carryHeight` (how high it lifts before it turns, m), and from phase 6 the hand orders. **Ports**: in **Run** (as every
machine), out **Busy**, **Alarm** (stuck), **Moved** (a count).

**By hand** (phase 6, question 6): the lifter's panel lists its points with what each holds; a click on a kind and then on
a put point is a move order ("3 Hull Plate: Lay-down Pad -> Press bay 1"), at most 8 a lifter, done before automatic
work. That is hand control without a joystick: the same carries, asked for by a player.

## 6. What the player sees and builds

- **Builds.** A Loader Arm is one part from the Assembler (Storage and Logistics, a new section Lifters), stood on a
  floor or a foundation beside what it serves, given power. A crane is rails and a bridge. A Lay-down Pad is a floor
  piece. Nothing is linked: a lifter serves what it reaches.
- **The reach view.** While a lifter is looked at or its panel is open: its ring or box in amber (as the bay is drawn),
  each point marked green with its name, red with its reason. A station's panel draws its fitters' reach over the bay.
- **Motion.** The arm's shoulder, elbow and wrist follow the hand (two-bone IK in the renderer, no state); the piece is
  drawn at true size in its paint, as a rider is. A fit still flashes the shell and plays the weld's end. A crane's
  bridge, trolley and cable follow the hook. All of it stops when the machine stops (look.md, point 2).
- **Look line.** "Loader Arm (Workshop): fitting a Wheel Hub to the Buggy, step 12 of 26 - 5 points in reach", "Bridge
  Crane (Medium): Hull Plate 6.0 x 2.4 m -> Press bay 1", "holding a Hull Plate - stuck: nothing in reach takes it".
- **The machine look.** A silhouette no other machine has (look.md, point 1): a turret base with a two-part boom and a
  gripper; white band at the pick side is not possible (it has no fixed sides), so the base carries hazard paint all
  round and the gripper is the orange "out". The tiers read as their ladder: a timber derrick with a chain, a blue cast
  arm, a yellow hydraulic arm, an olive armoured mount, a dark arm with a ring joint.
- **Text.** Refusals, ASCII, one reason each: "too heavy: 2.4 t, this Arm carries 1 t", "too long: 6.0 m, this Arm takes
  5 m", "out of reach: 6.4 m, it reaches 5 m", "its far end has no rail", "no power".

## 7. Net and saves

| What | On the wire | A late joiner | A save |
| --- | --- | --- | --- |
| The parts | new part files (ids move): protocol bump | the Full | by name |
| A move | `CBodyKind.Lifts` (new, at the end): a body's lifters that changed, each: part, state, why, the piece (`ConstructStorage.WriteDef`: an id or `Extrusion.WireMark` and its key), health, paint, from and to as cm in the construct's frame, two packed turns, `Dur` and `T` in ticks, the step, `Dest`, `Tag`, `Flags`. About 40 bytes, sent when a move starts, ends or sticks; never per tick | after the Full, with `T` as it stands | a `LIFT` block after `LOGI`, only when a hand holds a piece: part, the piece's name, health, paint, destination part plus one, tag, flags, the step |
| A fit by a lifter | the station's own message (`CBodyKind.Station`: `Fitted`, `LastPart`, `FitSeq`) and the body's structure, as today | as today | as today: `Claim` is not saved |
| Points, reach, rail runs | nothing: both peers work them out on a rebuild | | nothing |
| Settings | part settings (`liftMode`, `carryHeight`) | the Full | by key |
| Hand orders (phase 6) | `CActKind.Lifter` (body, part, op, kind, count, from, to), checked as a finger on a console; the list rides `CBodyKind.Lifts` | with the lifts | in the `LIFT` block |

- **A piece in the hand at a save** loads in the hand at rest, and the lifter's first decision is where it goes: the step
  if it is still unfitted and ready, else the hold or a pad. An older save has no block; an older build skips it.
- **Protocol**: one bump in phase 1 (now 271; the merger renumbers), again in phases 2 (tier parts), 3 (pads, a held
  piece at `outAt`), 5 (crane parts) and 6 (the action). Phase 4 changes no bytes if a lifter's edge is worked out on
  both peers; verify.
- **Blueprints** keep a lifter as a part with its settings. The Production Showcase gets its arm in phase 6.

## 8. Performance budget

Targets, to be measured by the builder with a throwaway probe that is removed (no new self-test sections):

| What | Budget | Allocations |
| --- | --- | --- |
| A world with lifters, none working | one list check a tick (`LiftOpen`, as `StationOpen`) | 0 |
| A lifter moving | a `T` add and a compare at 10 Hz: under 0.1 microseconds | 0 |
| A decision (idle, something changed) | at most 16 points, a `Takes` each; a station's offer walks unfitted steps from `Cursor`, at most 64 looked at: under 5 microseconds; at most 4 decisions a step a world | 0 |
| Fits | one `FitOne` a tick a world, whatever the fitters | as today |
| `LiftMap` | on a rebuild of a construct with lifters: ports within reach, under 0.1 ms at 32 lifters | 0 after the first (fixed arrays) |
| The wire | 40 bytes a carry: 8 Plasma arms at a station, about 400 bytes a second | 0 |
| A client | path and IK only for lifters drawn; nothing for one out of view | 0 |

**To measure and report:** the worst server tick during a 200-step build with 8 fitters; bytes a second to a client in
that build, split into the lifters' and the body's own (see the first risk); bytes allocated over 600 steady ticks
(expect 0); start-to-drivable for the Buggy with 0, 1 and 4 arms.

## 9. Phases

Each: its own branch and worktree, WIP commits with `[skip ci]`, the final push without, a build that passes, a by-hand
run reported with what was not verified. No self-test sections are added. `production.md` gets a "Lifters" section (listed
in `CONSTRUCTS.md`) at the end of phases 2, 4 and 6.

| # | Title | Work | Files | Stop point | Try by hand |
| --- | --- | --- | --- | --- | --- |
| 1 | **The lifter and the station's fitters** | `storage.lifter` parsed; `CLift`, `LiftOpen`, the 10 Hz step, the path; `StationJob.StepAt`, `Claim`, `Offer`, one fit a tick; `CBodyKind.Lifts`; the tier 1 Loader Arm from the generator with a recipe; plain drawing (the piece on its path, the boom as one box pointing at it). Protocol bump | new `Source/Constructs/ConstructLifters.cs`; `ConstructStation.cs` (`Cost`, `Orders`, `StepJob`, `Cancel`, `Release`), `ConstructParts.cs`, `ConstructNet.cs`, `ConstructWorld.cs`; new `Source/PartKit/Generators/LifterParts.cs`, `Constructs/Parts/Lifters`, `Categories.json`, `Generators/Assembling.cs`; `Render/RouteRender.cs`; `Net/ServerBrowser.cs` | A Buggy built at a station with one arm beside the bay finishes sooner than without, and a joined client sees the same carries and counts | The window tool's `station` verbs with and without the arm (times to a file); `--net host` and `join`; Cancel with a piece in the air |
| 2 | **Look, panel, tiers, saves** | Tagged segment boxes and the IK draw; the reach view; look line, machine screen, Busy, Alarm and Moved; `liftMode`, `carryHeight`; the five tiers through `MachineTiers.cs` and `TierStyle.cs`; the station panel's Fitters line; the `LIFT` save block; power draw. Protocol bump | new `Source/Render/LifterRender.cs`; `ConstructLifters.cs`, `LifterParts.cs`, `MachineTiers.cs`, `TierStyle.cs`, `ConstructConfig.cs`, `MachineScreens.cs`, `UI/UiStation.cs`, `Worlds/WorldSaveBodies.cs`, `ConstructPower.cs`, docs | Five arms stand side by side and read as their tiers; a world saved with a piece in a hand loads and finishes its build | `--window-shot` of the five; `--gen-parts lifter_parts --check`, `--check-construct-parts`, `--look-report`; save and load mid-build |
| 3 | **Carrying between points** | `LiftMap` (points in reach on a rebuild); the Lay-down Pad and its take-in of loose pieces (`TakeDropped` shared); a machine holds its piece at `outAt` for a lifter; puts to a press bay, an Assembler, a rack, a pad, a hold; the plain rule (wanted in reach, else storage in reach); Stuck. Protocol bump | `ConstructLifters.cs`, `ConstructConveyor.cs` (`PutMade`, the pad as a conveyor's end), `ConstructPress.cs` (the piece held at `outAt`), `ConstructStation.cs` (`TakeDropped` moved out), `Generators/PieceRackParts.cs`, `ConstructRackPack.cs` if an open hold needs it | An Extruder with no conveyor, an arm, a Press and a pad: blanks go into the bay, pressed parts onto the pad, with no link laid | That line in a window, host and client; a piece too heavy and one out of reach show their reasons; a 3 m plate set down on the pad with the physics gun is taken in |
| 4 | **On the logistics network** | A lifter as a node and its point pairs as edges of `LogiMap`; next hops, `Assign`, `RouteTo`, chains and fetches through a lifter; a Router sends toward a lifter's pick point; the station's large parts: asked for, delivered to a pad in a fitter's reach, fitted from it; pads in `ConstructReserve` | `ConstructLogistics.cs`, `ConstructLogisticsPlan.cs`, `ConstructCrafting.cs` (`RouteTo`, `Holds`), `ConstructLifters.cs`, `ConstructStation.cs` (`Orders`, the bill's rows), `UI/UiStation.cs`, docs | A station build whose blueprint holds a part longer than the hold orders it, receives it by lifter and fits it, with nobody touching it | A scratch blueprint with one 6 m pressed plate; the terminal's order row for a part that crosses a lifter; the `logi` verbs' map dump lists the lifter's edges |
| 5 | **The Bridge Crane** | Crane Rail and Crane Bridge parts in three classes (`LargeSystems.cs` or `LifterParts.cs`); the run walk on a rebuild; the box reach and the bridge path; its drawing (bridge, trolley, cable); its refusals. Protocol bump | `ConstructLifters.cs`, new `Source/Constructs/ConstructCraneRails.cs`, `LifterRender.cs`, the generator, `Categories.json` (the existing section Cranes and lifts), `Generators/Assembling.cs` | A Medium crane over a 16 m hall carries a 6 m plate from a Large Press to a pad and fits it at a station in its box | A window run of that hall; a rail section removed mid-carry (the crane stops, the piece stays in its hand, the reason shows); a bridge with one run |
| 6 | **Hand orders and the Showcase** | The lifter's panel (points, what each holds, move orders), `CActKind.Lifter`, orders in the `LIFT` block; the Production Showcase gets an arm at its station and one lifter-served press; the code guide. Protocol bump | new `Source/UI/UiLifter.cs`, `App/Program.SandboxLifter.cs`, `Game/GameSandboxLifter.cs`, `ConstructLifters.cs`, `ConstructEdits` (the action), `PartKit/Generators/ProductionShowcase.cs`, docs | A player moves three plates from a pad to a press bay from the panel, on a client | `--window-shot` with `panel` and `click` on the lifter; the Showcase built and its station build timed |

Order: 2 needs 1; 3 needs 1; 4 needs 3; 5 needs 3; 6 needs 2 and 3. `ConstructLifters.cs` is edited in every phase and
`ConstructStation.cs` in 1, 3 and 4: one phase at a time. Phases 3 and 4 touch `ConstructConveyor.cs`,
`ConstructLogistics*.cs` and `ConstructPress.cs`, which smart logistics phase 3 and the Press phases also touch: start
them only when those are on main. **Phases 1 and 2 are shippable alone** (faster station builds, nothing else changed).

## 10. Risks

- **A fit resends the body.** `FitOne` marks the body in `StructureDirty`, and a structure change goes out as the body's
  Full (read from the code's shape, not measured). At a part every 2 s that is cheap; at five fits a second on a 400-part
  body it may not be. Phase 1 measures it. If it is too much, a held body's structure is sent at most 4 times a second
  (the arms' carries are still shown, and the parts appear with the next send), or the fit gets a patch message of its
  own: that would be a phase of its own.
- **Pieces through walls.** A scripted path ignores what stands between two points. The carry height and the arm's
  round-the-base path keep it tidy in an open hall; a check on the rebuild (one ray a point from above the base: "no clear
  way: the Sawmill stands between the Arm and the pad") is cheap and belongs in phase 3 if question 2 says so.
- **Step places from the scratch build.** A step whose place depends on a joint's pose or a wheel's travel is taken at
  rest; the reach test uses the part's centre, so a long part can reach out of the ring while its centre is inside it.
  Harmless (nothing collides) but it can look stretched; the arm's IK clamps at full reach.
- **Moving bases.** Points and paths are in the construct's frame, so a lifter works on a mothership under way; the
  station's body is frozen in the world, so a station on a moving construct is already outside what phase 1 of the
  station handles. Not made worse, not solved.
- **A machine that holds its piece at `outAt`** (phase 3) changes what a press with no conveyor does today (it drops the
  piece as a body). The rule must apply only when a lifter has that point; with none in reach it drops as before.
- **Two lifters, one point.** Claims are per piece and per step, and a put point's `Takes` is asked again at the place; a
  lifter that arrives at a point just filled waits Stuck for 2 s and looks again. No deadlock is possible because nothing
  holds a point, only a piece.
- **Numbers.** Every figure in sections 3.2 and 3.4 is a first guess. The Buggy takes 77 s today; the measure in phase 1
  says whether a Scrap arm is worth building or the table needs moving.
- **Not in this design:** a lifter on a vehicle (a crane truck: the physical parts do that today), picking loose bodies
  off the ground outside a pad, roll-out of the finished vehicle, a lifter working across two constructs (the network is
  one construct by the user's answer), bulk and items (chutes carry those).

## 11. Questions for the user

One at a time; the first option is the recommendation and what the phases assume.

1. **What is an arm, underneath?**
   - A (recommended): a **scripted machine**: one part, the piece moves on a computed path, nothing is simulated. It
     always works, costs no tick time and little traffic. The physical crane parts stay for building by hand.
   - B: a **physical arm** built from joints and driven by a controller part. It can drop, sag and jam; about twice the
     work and a solver cost for every arm.
   - C: A now, and later a controller part that lets a hand-built jointed arm take the same work.
2. **The piece in an arm's hand passes through players, vehicles and walls, as pieces on a conveyor do. Is that
   acceptable?**
   - A (recommended): yes, with a check when the base is built that names anything standing between the arm and a point
     ("no clear way").
   - B: yes, with no check (cheapest).
   - C: no: the carried piece must be solid and push or stop at things (this leads back to question 1 B for the carry).
3. **How is a crane built?**
   - A (recommended): **rails and a bridge**: lay Crane Rail sections in two runs and set the bridge on them; its reach is
     what you built.
   - B: **one part with size settings**, as the station's bay (span, travel, height in the configure tool); cheapest,
     nothing to assemble, the cost does not grow with its size.
   - C: a **monorail**: one overhead rail and a hoist that serves the strip under it; simpler to build, covers a line and
     not a floor.
4. **Should heavy or oversized parts need a lifter at the station?**
   - A (recommended): no: the station alone still fits anything, slowly (2 s plus its mass, 10 s at most); lifters only
     make it faster and bring what the hold can't take.
   - B: yes: a part over the station's own limit (say 500 kg, or longer than its hold) waits for a lifter that can carry
     it, so big builds require cranes.
5. **How does a lifter know what to carry between machines?**
   - A (recommended): **by itself**: it serves every point in its reach by the Routers' rule (to where it is wanted, else
     to storage), with a mode setting to restrict it.
   - B: the player **links** each pick point to each put point with the linking tool, as a conveyor is laid.
   - C: only from **orders given at its panel**.
6. **Hand control.**
   - A (recommended): **move orders on the lifter's panel** (this kind, from here, to there), in the last phase.
   - B: also a **cab**: a seat from which a player steers the hook with the move keys, grabs loose pieces off the ground
     or a truck, and sets them down (a phase more).
   - C: none: lifters are automatic only.
7. **Tiers.**
   - A (recommended): the Loader Arm has the machines' **five tiers** (reach, payload, speed); the Bridge Crane has
     **three size classes** and no tiers.
   - B: both have five tiers (fifteen crane parts and their recipes).
   - C: arms in three size classes like the crane, no tiers: a bigger arm is the better arm.
8. **Which comes first after the station's arms (phases 1 and 2)?**
   - A (recommended): **carrying between machines and the network** (phases 3 and 4), then the crane.
   - B: **the crane** straight after phase 3, the network later: big pieces move sooner, but only between points one
     lifter reaches.
