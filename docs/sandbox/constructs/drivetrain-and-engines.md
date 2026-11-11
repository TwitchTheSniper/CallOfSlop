# Drivetrain and engines

Covers drivetrain part families, suspension, modular engines, tracks, big drive. Part of the construct guide: index in ../CONSTRUCTS.md.

## Drivetrain parts (the families)

Board item dev-sandbox-drivetrain-parts-dp01 (the developer's note, 2026-10-29). Code: `Source/Constructs/ConstructDrive.cs`
(the drive line, the engine each tick), `ConstructDriveParts.cs` (the blocks), the wheels in `ConstructPhysics.cs`. Data:
`Constructs/Parts/Drivetrain` (53 files) and the rescaled Wheel, Suspension, Engine, Transmission, Differential and Steering
Pivot. The scale is the developer's "Realistic" chart ([Structural parts](parts-and-data.md#structural-parts-the-scale-chart)).

### The drive line

The engine must touch a working **transmission**; from it the drive runs through working **differentials** and
**transfer parts** linked to it, each only through its **ports** (a shaft in one end and out of the other, never its side),
to the **suspensions** bolted to a differential's or a **hub**'s ends, or on the leaf of a **steering pivot** that a **U
joint** or **CV joint** feeds. Each driven wheel keeps its reduction (the gearbox's gear times the chain, belt and gearbox
ratios on its way, times its differential's or hub's final drive), what is left after each part's losses, the most
torque its line carries (a belt slips above it) and its **gates**: a clutch, a transfer case's front output, a U or CV
joint's angle. The torque is shared between the drive groups that touch the ground and inside each group by its kind:
**open** (a wheel in the air takes it all: 10% reaches the other, and no wheel is driven harder than the one that grips
least), **limited slip** (60% to the wheel that grips, half as hard again), **locked** (all of it; a locked axle with both
wheels down scrubs the turn: it damps the yaw), **lockable** (open or locked by the linking tool's R or a wire).

**Half shafts.** A drive shaft from a differential output (or a gearbox, transfer or engine output) may end on a suspension's
**Drive in**: see [Half shafts](#half-shafts). The wheel on that suspension is then driven as if it were bolted on: the
torque split, ratio, losses and gates are the line's own, and a straight half shaft loses nothing.

**Forward** is `Construct.DriveFwd`, worked out once with the caches (`BuildRollSense`): the facing of the seat that controls it through a Driving Module (`Construct.ControlSeat`), else the first seat on the body (a seat
attached freeform counts), or the seat's right when the wheels roll across the seat, so it is never square to the wheels. Each
wheel's `RollSign` comes from it, and `Construct.RollAxis` is the axis its spin turns about, so a positive spin rolls it forward
however it was mounted: left or right, rolled on its socket, attached, mirrored. Physics, the drawn spin, tracks' rollers and a
tread's speed all use it. A wired or skid-steered drive still sends the sides opposite ways through `SideSign`, on purpose.

**Steering differentials** (kind `steering`: the Steering Differential, the Wide Steering Differential, and the Medium, Large and
Capital Steering Axle). A deliberate choice of skid steering, on wheels, Track parts or freeform tracks, bolted on or on half shafts.
Throttle goes to both sides evenly; steering adds to one side and takes from the other (`throttle - steer x SideSign`, clamped), so a
full steer at zero throttle counter-rotates the sides and the vehicle pivots on the spot, and the steering alone starts an engine that
needs a starter. Which side a wheel is on is measured from the differential, not the centre of mass.

- **Against an open differential:** no grip limit and no wheel in the air stealing the drive. Every touching wheel gets its share, like a
  locked axle (`Share`), but it doesn't scrub the turn, since turning is what it does. A side in the air spins free.
- **Braking:** the brakes win. Brake with steering stops both sides straight, and a neutral vehicle stays parked; with any steering
  held the parking hold lifts so it can pivot.
- **With steering pivots too:** both steer. The sides do the pivoting at a standstill and hand over to the pivots as it moves: the
  sides' share of the steering falls to 15% by 8 m/s (`PivotFade`). In reverse the pivots still steer as a car does.
- **Sliding:** while steering, tyres have 15% of their grip sideways (`TurnGripWheel`), free wheels 3% (`TurnGripFree`), and tracks
  45% as before, coming back with speed.
- **Automatic skid steering stays:** a construct whose driven wheels don't steer still skid-steers through any differential, so
  shipped crawlers keep working. The new part adds the even drive, its own sides, and skid steering alongside pivots.

**The Steer input.** Every size takes one data link, `Steer`: -1 to 1, where 1 turns right, the same sense as the seat's D key
(`steer` here and `CPart.In` agree, both go through `ConstructDrive.WheelSteer`). While a wire is in it wins over the seat, even at
0, for that differential's wheels; pivots still follow the seat. It needs no seat: with nothing else asking, a non-zero signal
starts and runs the engine (`SteerRuns`), unless the engine's own run wire says off. A non-zero signal wakes a sleeping body. Two steering differentials take a wire each.

**Steering type** (the configure tool's `steerKind`, on every size, saved and in blueprints): how the sides steer, with the seat or the wire, in proportion to how far it is steered (`ConstructDrive.SideDir`, `InnerHold`).

| Type | Inner side | Outer side | No throttle | Measured: a Tracked Crawler from 19.3 m/s, full steer for 3 s |
| --- | --- | --- | --- | --- |
| Reverse (default, and what the part always did) | `throttle - steer`: stops at full throttle, backwards at less | forwards | pivots on the spot | radius 39 m, 50% of its speed lost |
| Drag | brake (steer cubed: half way is a light touch), and it loses the throttle | keeps the throttle | turns about the stopped side | radius 6 m, 95% lost |
| Clutch | coasts: no drive, no brake | keeps the throttle | nothing: the steering asks the engine for nothing | radius 123 m, 9% lost |

The look line reads `Steering Differential: Reverse: inner side backwards, pivots - wheels on one side only: it can't turn`, and adds `wired (1 is right)` with a wire in.

**Why it won't go** (the HUD's red line): "the electric motor needs a working battery", "a steered wheel needs a U joint or
CV joint to be driven", "a stub axle carries no drive: fit a hub or a differential", "the drive shaft 1 m is fitted by its
side: the drive goes in and out of its ends", "the drive shaft 1 m is wrecked", "a caster wheel can't be driven", "a
piston can't carry the drive", besides milestone 2's. A **drive note** (orange, sent to clients) says why a built line
isn't driving right now: the clutch is out, the U joint is bent past its angle, the belt is slipping, the battery is flat,
a diesel needs a battery for its starter; "shifting" and "spooling up" in grey.

### The engine each tick

- **rpm**: from the driven wheels touching the ground (speed over radius times the reductions), never below idle; pulling
  away the clutch slips and the engine revs towards its peak; in the air it revs freely; it can't go much past the
  limiter. **Torque**: the peak times the curve at that rpm (petrol rises to its peak; diesel strong low down and falls
  off early; electric flat to its peak then constant power; turbine low at idle; flat), times the throttle (a turbine
  times its **spool**, which takes its `spool` seconds), and nothing at the limiter or during a **shift** (its `shift`
  seconds). Fuel use grows with the throttle and the rpm (a turbine burns at idle too); an electric motor draws the
  batteries (its power over 0.9); a hamster wheel needs nothing.
- **Gears**: manual and sequential boxes take the driver's **R** (up) and **sprint** (down) when nothing is wired in, the
  linking tool's R or a wired signal as before; an **automatic** goes up at 90% of the limiter and down when the lower gear
  would stay under 70% (a wired signal caps its highest gear); a **CVT** holds the engine at its peak within its range; a
  **reversing** box backs up at its one ratio (others back up in their reverse ratio, 1.1 times first). A gear picked
  standing still costs no shift time.
- **Starting**: a diesel or a turbine (`"starter": true`) needs a battery: without one, "It won't start". An electric
  motor runs while a working battery's links reach it with charge. An engine with nothing to drive still runs (a power
  take-off).
- **Wheels**: grip on terrain (dirt) is 85% of hard ground times the tyre's `offroad`; `side` scales the sideways grip
  (an omni wheel slides, a track unit turns on the spot); `roll` is a share of the load; a wrecked tyre with `flat` stays on
  at three quarters of its radius with half its grip. A **track** unit is one part with several contact rays along its
  length (6 on the 5 m Track); a construct whose driven wheels don't steer **skid-steers** (the steering drives one side
  forward and the other back). Air drag (0.006 v squared) tops out the tall gears.
- **Suspension**: a spring carries at most its `maxLoad` (kg a wheel): more and it sags and bottoms. An **adjustable**
  strut's presets (R) set its stiffness, damping and, with `hold`, a ride height; an **active** (hydraulic) strut holds the
  height its wire asks (0 low, 1 high, unwired half way, 0.36 m between) and draws 150 W on an electrical construct.
- **Drills**: a shaft into a Drill Module's Drive in turns it, with or without a transmission, and it takes its torque back
  from the engine: [The drill's buffer and shaft](gathering-and-salvage.md#the-drills-buffer-and-shaft).
- **PTO**: a turntable, motor hinge or piston bolted to a power take-off on the drive line runs from the engine, only while
  it runs, with no battery. An unwired turntable on a take-off turns with the engine (its rpm's share of the limiter;
  backwards on a reverse throttle); a wire into it still sets its speed.
- **Standalone parts** (board item cs03, decision 32): nothing waits for the construct to be a "vehicle". The drivetrain
  builds with or without a seat (`Construct.HasPowertrain`: an engine with what it runs on and a transmission on it;
  `CanDrive`: that and driven wheels). The engine runs for a driver or a signal: an engine has two data ports, **run**
  and **throttle**. Wired on run alone it runs at that signal (the old single input: a lever is the throttle); with the
  throttle port wired too, run (0.5 or more, or unwired) starts and stops it and throttle sets the throttle (a lever, a
  number from the logic). On an **anchored** construct (a foundation, pinned, frozen) the engine still runs and the
  wheels don't roll it: each driven wheel turns at the engine's speed through the gear (`ConstructDrive.AirOmega`), and a
  turning wheel's tread drives what sits on or leans on it through the contact friction (`ConstructPhysics.TreadVel`):
  a crate on it, a vehicle against it, a row of them is a conveyor. A **suspension with nothing on its axle, pointing
  down**, is a spring foot (`CSpring`): a raycast spring like a wheel's with a gripping foot, so a platform on
  suspensions stands on its springs, and players standing on it weigh it down (a landing knocks it). Sleep: an anchored
  construct whose engine runs, runs down or still turns a wheel is stepped every tick (`ConstructPhysics.EngineLive`,
  and what sits on its turning wheels is woken); once all has stopped it costs nothing again. Not yet: a spring foot on
  an anchored construct can't give (the body can't move; that needs a sprung joint), and a turning wheel only drives
  bodies whose pieces touch it, not a body it is itself pressed into.
- The configure tool is still to come: the **HUD** shows the speed, rpm, torque, gear and fuel while driving (and "R gear
  up, Shift gear down" for a manual), and the **look line** an engine's rpm, torque and curve, a gearbox's ratios and kind,
  a differential's kind and ratio, a transfer part's ratio or state (2WD/4WD, engaged), a wheel's size and grip (FLAT), a
  suspension's travel and preset or height.

### The families

| Family | Parts (no single best: mass, grip, power, fuel and toughness trade off) |
| --- | --- |
| Wheels | Wheel (0.8 m road), Small (0.6 m), Large (1.1 m), Off-Road (grips dirt, worse on roads), Monster (2.4 m, 220 kg), Solid (no puncture, armour 25, less grip), Slick (most grip on hard ground, slides on dirt), Spiked (bites dirt, skates on roads), Caster (0.4 m, swivels, can't be driven), Omni (slides sideways), Tank Road Wheel (steel), Track (5 m) and Short Track (3 m), sprockets at the back |
| Suspensions | Suspension (0.3 m, 600 kg), Stiff (0.12 m), Long-Travel (0.6 m, soft, light loads), Leaf Spring (3 t a wheel, tough), Adjustable (soft, medium, stiff, high, low), Rigid Mount, Hydraulic (active); Pogo Struts, Swing Arms, Double Wishbones and Trailing Arms in eight sizes: [Suspension types](#suspension-types) |
| Engines | Engine (0.5 m petrol, 260 N m), Small Engine (0.4 m single, 70 N m, 9000 rpm), V8 (1 m, 520 N m, thirsty), Diesel (1 m, 820 N m at 1800, 3200 rpm, starter), Electric Motor (380 N m from standstill, battery), Turbine (700 N m to 12000 rpm, spools 2.5 s, thirsty, starter), Hamster Wheel |
| Transmissions | Transmission (3-speed manual), 5-Speed, Automatic (4, 87% through), CVT (84%), Reversing, Low-Range (12:1 and 8:1), Sequential (6, 0.12 s shifts) |
| Differentials | Differential (open, 1.1 m), Limited-Slip, Locking, Wide Differential (open, 1.6 m), Live Axle (locked, 1.6 m) |
| Transfer | Drive Shaft 0.5, 1 and 2 m; U Joint (15 degrees), CV Joint (45); Right-Angle, T and Reduction (2:1) gearboxes; Transfer Case; Chain Drive 1:1 and 2:1, Belt Drive (0.67:1, slips over 900 N m); Clutch; Power Take-Off; Wheel Hub (4.1:1); Stub Axle |
| Tanks, power | Small (15 l) and Large (150 l) Fuel Tank, Battery Pack (20 MJ) |

### Wheels: the rim and the cylinder

`Source/Constructs/ConstructWheelShape.cs`. The spring stands on rays, so a wheel still carries weight, grips and steers as it
did; what is new is that it has a shape.

**The rim.** One ray at the axle says what is under the hub. Before this a wheel was that one ray: it saw a kerb only when
its axle was over it (the wheel went into the kerb, then snapped up), lost the ground over any gap under its axle, and a big wheel's
hub followed every bump. Now, when the rim matters, four more rays go down at 0.5 and 0.87 of the radius fore and aft of the
wheel's place next tick, and two at the tread's edges. A ray that finds ground `s` metres from the axle asks the hub
to ride `sqrt(r*r - s*s)` over it, and the highest ask wins, up to the spring's travel.

The rim matters (`RimWanted`) when something stands near the wheel (the cylinder's quick test saw it last tick), when the ground
falls away under the axle, and on terrain when the wheel is wider than a cell. On an open road or a deck a wheel still costs
one ray. A vehicle of more than 32 wheels asks 32 of them a tick, in turn, and holds the rest's last answer. So:

- A kerb lifts the wheel from where the rim first meets it, by as much as a circle would. Where the height jumps between
  two rays its edge is found by halves (3 more rays), so the rise is smooth, and a damper never sees more than 2.5 m/s
  from it (a tyre takes the rest of a blow).
- A step higher than 0.6 of the radius isn't climbed: its rays are ignored and the cylinder meets it, as a wall, its push level: the
  block's top edge can't throw the wheel up and over.
- A gap narrower than the wheel is bridged, the hub dipping by what the circle would. A thin edge under the tread carries it.
- A tread's edge rides up a low kerb met side-on (0.12 of the radius, a tyre's shoulder); higher, the side of the wheel stops.
- A big wheel on rough ground rides the tops of the bumps.
- A rail too thin for the rays still lifts the wheel: the cylinder's point asks the spring for that height next tick (`RimAsk`).
- On flat or even ground the rim's rays don't run, or the axle's wins, so a car on a road does exactly what it did.

A client draws its wheels with the same rays (on vehicles of up to 32 wheels; more, the axle's only).

**The cylinder.** A wheel on a suspension has one extra leaf in the [compound collider](network-and-scale.md#compound-collision), at the hub as drawn:
its travel, its steering, an arm's arc and lean ([Suspension types](#suspension-types)). The leaf's bounds hold its whole travel,
so its tree is never rebuilt as the hub moves; only the narrow test reads where the hub is now. It goes through the hull test
as a **sixteen-sided prism** (32 corners, the hull limit; 18 faces, within 1% of the true radius, at any size), sized from the
wheel's radius and width. The wheel's boxes stay in the tree for the physics gun's step and ray hits, and collide no more.

- Walls, kerbs too high to climb, other vehicles, loose bodies and steep ground stop the wheel. The knock goes to the body at the
  contact, not through a steering pivot.
- What the rays carry isn't met twice: a point pushing the wheel up by at least 0.4 (the steepest step it climbs) is dropped while the spring
  has travel left, and one pushing it straight up (0.85) even when bottomed, where the rays' hard stop has it. A body too
  light to count as a deck (a crate) meets the whole cylinder.
- A free wheel's contact with what can stop it has low friction (0.15, it rolls). Against a crate or on an anchored
  base the tread drives what it touches (`TreadVel`, from the hub now). Friction against the tread's speed on a wall ran cars up the
  wall and over.
- A piece that is further than the cylinder's sphere, or wholly under a wheel the rays carry (a floor, a deck's plates), never reaches
  the hull test, so standing on a deck adds a few comparisons.

Not cylinders: tracks (one ray per contact point, their boxes as before), a wheel with no suspension, and a big construct's wheels
against the ground's triangles when the construct asks for the ground leaf by leaf (`GroundLeafByLeaf`: over 900 cells). Players
and bullets still meet the wheel's boxes at their place at rest.

**Any order.** A wheel is sprung when its axle socket is linked to a working suspension's and its axle lies level, whichever came first: wheel on the suspension on the vehicle, or suspension on the wheel and the piece onto the vehicle, by the hands, socket attach, point attach or freeform, on the grid or off it. A piece with wheels may go on off the grid; on a suspension welded on at an angle a wheel still travels straight up and down in the body's frame (`CWheel.Up`), and an arm's arc is left out, so it moves in a line. "Wheel orders" drives them.

**The tyre's settings.** Every wheel, track and tyre gets them from its role (`ConstructConfig.AddWheelSettings`), each starting
at its part file's number, so nothing changes until someone moves one; a part file only lists one to change its range or text.
They live on the part like all settings: blueprints, world saves and the construct message carry them.

| Setting | Key | Default | Range | Does |
| --- | --- | --- | --- | --- |
| Dry grip (g) | `friction` | `grip` times the material's `friction` | 0.05 to 4 | The friction circle on hard ground; its share of the default also scales the cylinder's contact friction (at most 2x) |
| Dirt grip (x) | `dirtGrip` | `offroad` | 0.1 to 3 | Times the dry grip on terrain |
| Side grip (x) | `sideGrip` | `side` | 0.01 to 2 | Times the sideways grip |
| Rolling drag (%) | `rolling` | `roll` | 0 to 50 | Share of the load |
| Bounciness (%) | `bounciness` | 0 | 0 to 100 | Less damper (at 100%: 10% on the way back up, 40% going in); the hard stop and a cylinder's knock give back 70% of the closing speed |

A flat tyre's half grip and extra drag come on top of these. "Tyre pressure" isn't a setting: it would only be dirt grip and
rolling drag moved together.

### The rescaled blueprints and the examples

The **Buggy** (and the Wired and Hood Gun Buggies) is 3.9 by 1.8 m on two 2.5 m square-beam rails, a cross member, a
deck plate and a wedge nose, 0.8 m wheels 2.9 m apart; the engine sits on the gearbox over the differential, its tank
behind on a fuel line; the steering pivots are 18 cm knuckles. The **Trailer** is a square-beam drawbar with a 1 m bed.
New examples: the **4x4 Truck** (8.2 m: a diesel, an automatic, a transfer case in 4WD, right-angle gearboxes onto a locked
live axle and an open wide axle with CV joints into its steering pivots, leaf springs, large wheels), the **Tracked
Crawler** (7.1 m: a V8 and an automatic on a wide axle driving two 5 m tracks, skid steering), the **Racer** (a V8, a
sequential box, a limited-slip differential, slicks on stiff struts) and the **Electric Cart** (2.6 m: an electric motor
under a battery pack, a reversing box, small wheels). The truck's and crawler's ladders are still the old 2 m Frame Beams
(their drive lines sit at a 0.5 m beam's height); everything else is on the structural parts.

### Self-test and pictures

The **Construct drivetrain** section (`Source/App/SelfTestConstructDrive.cs`): every part file loads and 15 bad drivetrain
blocks are refused for the right reason; the buggy, truck and crawler match the chart; each wheel, suspension, engine and
transmission changes the drive measurably (distance on hard ground and dirt, the turn, ride height and roll, speed off the
line and the top speed, climbing a 15 degree ramp with a small engine; slicks faster on roads and slower on dirt, off-road
tyres faster on dirt, a turbine slow off the line then faster, a hydraulic strut's height on a switch, the adjustable
presets, an automatic shifting, a CVT at its peak, a reversing box as fast back as forward, a sequential box's shorter
shift, low range climbing furthest, a diesel without a battery refused); a rear wheel in the air gets 5%, 60% or 100% of the
drive through an open, limited-slip or locked differential, and the locked one turns less; the drive reaches a wheel
through every transfer part kind with the right ratio, and is refused with its reason for a steering pivot without a U
or CV joint, a stub axle, a shaft fitted by its side and a wrecked shaft; a clutch on a switch, the truck in 2WD and 4WD,
its U joints losing the front drive at full lock (CV joints keeping it), a turntable on a PTO turning only with the engine;
the five examples drive and turn (the crawler on the spot) and the racer is the fastest; a client gets the rpm and the
note; eight vehicles driving at once allocate nothing (about 0.2 ms a tick in the container).

### Choices made (the developer may change them)

1. Gear ratios are **reductions** now (the old Transmission's 0.6/1.0/1.4 speed multipliers became 4.4/3.0/2.1), and a
   differential has a final drive (4.1 on the buggy's).
2. A **steered driven wheel needs a U or CV joint** between its axle and the steering pivot (milestone 2 let a pivot on
   a differential drive); a U joint loses the drive past 15 degrees of lock.
3. **Dirt** (terrain) grips 85% of hard ground; the old rolling resistance (a share of the speed every tick) became a share
   of the load plus air drag, so tall gears and big engines matter; the open differential's "no harder than the least
   grip" keeps a car straight at the grip limit.
4. A wrecked **tyre goes flat** and stays on (the shipped wheels); shoot the suspension to drop a wheel.
5. **Shifting by keys**: R and sprint for an unwired manual or sequential box (sprint isn't used while driving).
6. The truck and crawler keep the old 2 m Frame Beams for their ladders (the structural parts' square beams are 0.25 m,
   below the drive lines' 0.5 m height); a later pass can move them.
7. 247 of the 250 part ids are used (structure 110, drivetrain 53, storage); more part families need 2-byte ids (done
   with the routed links: part ids are two bytes since protocol 90, up to 4,096 types).

## Suspension types

Data: `Constructs/Parts/Suspensions` (36 parts) and the demo buggies, written by `tools/constructs/suspension_parts.py` (its
tables at the top are the balance; `--check` compares; `MENU` there is the one place that names their menu group). Code:
`Source/Constructs/ConstructSuspension.cs`, the rigged boxes in `ConstructRender.RigOf`.

**Sizes go by wheel.** Every kind comes in eight sizes, and the description says which wheels a size takes and what it carries:

| Size | Wheels | Carries a wheel | Travel | Spring |
| --- | --- | --- | --- | --- |
| Kart | 0.2-0.3 m | 150 kg | 0.12 m | 3.2 Hz |
| Light | 0.3-0.45 m | 400 kg | 0.2 m | 2.6 Hz |
| (no word) | 0.4-0.55 m | 1 t | 0.3 m | 2.2 Hz |
| Heavy | 0.55-0.8 m | 2.5 t | 0.45 m | 1.8 Hz |
| Truck | 1.2 m (Monster) | 8 t | 0.8 m | 1.4 Hz |
| Medium | 1.5 m (Medium Tyre) | 60 t | 1 m | 1.4 Hz |
| Large | 6 m | 3,000 t | 3 m | 1.1 Hz |
| Capital | 24 m | 60,000 t | 8 m | 0.9 Hz |

Nothing refuses a wheel for its size: the sockets fit, and the rating is what bites. The new kinds are gas struts (`ride`), so
each rides half way down its travel whatever it carries up to its rating, its arm level; past it, it sags and bottoms, as any
suspension does. The spring's stiffness comes from the load (`ConstructPhysics.Wheels`), so a size bounces at its frequency
from a light body to its rating. Stiffness and damping are configure tool settings on all of them.

**The kinds**, and how each mounts:

| Kind | Mounts by | Moves |
| --- | --- | --- |
| Suspension (plain; Kart, Light, Heavy and Truck added to the older parts) | `mount` (side), `top` | Wheel straight up and down beside the bracket. Nothing else moves. |
| Pogo Strut | `mount` (its top end, under something), `side` (its cap), `inner` (beside the hub, for a differential's end or a steering pivot's leaf) | Wheel straight along the strut: the rod slides into the tube, the coil closes up. |
| Swing Arm | `mount`, `mount-high` (chassis side), `top` | Arm hinged on the chassis side, wheel fixed to its end: the wheel swings on an arc, leans with the arm, and the track changes. A damper stands on the arm. |
| Double Wishbone | `mount`, `mount-mid`, `mount-high`, `top` | Two arms, an upright between them: the wheel moves on the arc but stays upright. |
| Trailing Arm | `mount`, `top`, `bottom` (the hinge's end) | Arm along the car, hinged ahead of the wheel: the wheel rises on an arc, moving fore and aft. Mirrored top to bottom, so rolled 180 degrees on its socket it trails on the other side of the car too (or leads); the Mirror Table does this itself. |

There is no Leaf Spring on a solid axle: the game has no beam axle (a Live Axle is a locked differential bolted to the body, and
each of its wheels springs on its own), so nothing would tie two wheels together. The shipped Leaf Spring stays as it is.

**The wheel's path** (`"path"` in the `suspension` block): `{"kind": "line"}`, the default, or `{"kind": "arc", "pivot": [x, y, z],
"axis": "+z", "length": m, "tilt": 0 to 1}`. `pivot` and `axis` are the arm's hinge in the part's frame; the hub swings in the
plane square to the axis. `length` is the arc's radius, when it isn't the hinge's distance from the wheel (a parallel-arm
upright's). `tilt` is how much of the arm's angle the wheel leans by. An arm too short for its travel (under 0.6 of it) is
refused. The raycast wheel keeps its role, and stays one ray and one spring: the ray still runs straight down the body and
`Compression` is still how far the ground pushed the wheel up. The arc only moves where that ray starts (`Construct.WheelFrame`,
along the arm, from last tick's compression), and so where the contact is, and where the wheel is drawn. `CWheel` keeps the arm
in the body's frame (`BuildArm`), so an arm works in any of the part's level turns and on a steering pivot's segment. A hinge
turned to run up leaves the wheel on a straight line; the look line says "stood on end: moves straight". Camber is drawn; it
doesn't change the tyre's grip.

**Boxes that move** (a detail box's `"rig"`; `"lever"` is the [Analog Lever](self-tests-and-history.md#the-analog-lever)'s handle): `"hub"` goes with the wheel; `"arm"`, with its `"pivot"`, turns by the arm's angle;
`"leg"`, with `"from"` (fixed) and `"to"` (moving, at rest), is drawn stretched between the two: `"pivot"` turns its moving end,
without one it moves with the hub, and `"span": [a, b]` draws that share of the way (a sleeve, a coil's ring). A leg's file box
gives its width, and stands straight at rest so a ghost and an icon look right. The solid boxes don't move: each part is a
hidden solid at rest, what collides and what the sockets sit on.

**Driving.** A suspension is driven by its link to a differential, a hub or a fed steering pivot's leaf, or by a [half shaft](#half-shafts) into its Drive in, whatever its kind, and refusals read as before ("a steered wheel needs a U joint or CV joint to be driven"). A
Pogo Strut goes on a differential's end or a pivot's leaf by `inner`. On a steering pivot the whole arm turns with the steering,
so a swing arm's wheel swings round the pivot on the arm's length: keep steered arms short. A Trailing Arm moves its wheel a cell or
more back along the car, which shifts the load towards the other axle: the Trailing Arm Buggy pulls away a little slower.

**Blueprints**: Pogo Buggy, Swing Arm Buggy, Wishbone Buggy and Trailing Arm Buggy are the Buggy on each kind, generated from it.

### Half shafts

Code: `Source/Constructs/ConstructHalfShaft.cs`, the drive solve in `ConstructDrive.cs`, the end drawn by `RouteRender`. A shaft link
that ends on a suspension's **Drive in** drives the wheel on that suspension. Every suspension that takes a wheel has one (plain,
Pogo Strut, Swing Arm, Double Wishbone, Trailing Arm, Leaf Spring, Rigid Mount and the adjustable ones, every size and the
Medium, Large and Capital classes; not hub motors, which need no shafts, or landing struts). The port is on the hub's inboard side:
at the hub's height on a bracket and a Pogo Strut, below the arm on a Swing Arm or Double Wishbone, so the shaft runs under the arm.
It stands on a socket of kind `port`, on which nothing bolts.

**Linking.** Start a shaft at an output as usual, and aim at the suspension or at its wheel: the linking tool ends at Drive in, with
the auto-route ghost, the picker, Free routing and hold to adjust all as for any input. The ghost and the port's marker sit where
the hub is now.

**The moving end.** The nodes stay on the output's side and the link's rules are checked at rest (hub at mid-travel, joints at 0).
Only the last point moves: `ConstructRoutes.LivePoint` follows the hub (`Construct.HubPoint`), so the last segment is a CV joint at
the hub and a plunge joint along its length, drawn with a sleeve and a boot each frame, and shots hit it where it is drawn. It
isn't refused as a shaft crossing a joint. Across a Steering Pivot it works too, with the suspension on the leaf (a joint-angle gate
of 45 degrees, a CV joint's). Any other joint is refused.

**Refusals**: "too far: half shaft reaches 4 m" (`HalfShaftReach`: the whole link, 4 x the part's longest side, at least 4 m; a half shaft's length comes from its arm, so this is the one link with a limit on its whole length, and it replaces the 8 m a segment, since a capital arm alone is 32 m),
"a half shaft goes into its hub at most 30 degrees off its line (38 here)", "at the end of its travel the half shaft is 34 degrees off
the hub (at most 30): come in straighter, or from further away", "a half shaft crosses a suspension's travel or a steering pivot, no
other joint: fit a U joint or CV joint there". The arm's mount counts as a part in the way: the clipping grace forgives the part
only at the hub (plus, on a bracket whose Drive in sits on its mounting face, what it is bolted to, since the shaft has to come through it).

**Driving.** A half shaft from a differential is part of that differential's group, so open, limited-slip, locked and lockable
behave as with bolted wheels. From a gearbox or transfer part it is a locked group of its own. From a power take-off it does nothing
(a look line says so). A wheel with no half-shaft line behaves as before. With no wheel on it the suspension's look line says "no
wheel on it: the half shaft ends here"; a shaft that drives nothing says why on its own look line.

## Modular engines

Build your own engine from parts that snap together; the game recognises it as one engine, with no wiring puzzle. Code:
`Source/Constructs/ConstructEngine.cs` (recognising it, the sums, the readout), `Source/Render/EngineFx.cs` (pistons, shake,
smoke), `Source/Audio/SynthEngine.cs`, `EngineSounds.cs`, `Audio.Engine.cs` (the sound). Data: `Constructs/Parts/ModularEngine`
(40 parts) and `Constructs/ModularEngine.json` (the sums' numbers), both written by `tools/constructs/engine_parts.py`:
change its `SIZES` table and run it, never the output. Self-test: "Modular engine", "Modular engine network".

### The pieces

Ten kinds in four sizes (Small, Medium x4, Large x16, Capital x64 by linear size): the look is exposed and industrial, with
open frames, columns round a visible pumping piston, a spoked flywheel and tall stacks.

| Piece | Where it goes | What it does |
| --- | --- | --- |
| Crank Section | end to end; bolts to a frame by a side | the crank: its length is the engine's |
| Cylinder | on a crank section's side | adds power; which sides make the layout |
| Carburettor | on a cylinder's head | fuel system: feeds its whole bank, x1 power, x1 fuel |
| Injector Rail | on a head | fuel system: x1.2 power, x0.8 fuel a kW |
| Turbo | on a head or a fuel system | x1.4 on its bank, 1.5 s of lag, x1.15 fuel |
| Supercharger | on a head or a fuel system | x1.25 at once, x1.25 fuel |
| Flywheel | on the crank's end | the output: the drivetrain's engine: gearbox or shaft on it, run and throttle ports |
| Starter Motor | on a crank's side or the flywheel | Large and Capital need one, and a battery to turn it |
| Exhaust Outlet | on a head or a fuel system | a smokestack stands on it or takes a pipe from it |
| Smokestack | on an outlet or another stack; bolts to a frame | the smoke leaves the top one, thicker and blacker with load |

A working engine is four parts: a crank, a cylinder, a fuel piece and a flywheel, plus a fuel tank touching any piece of it or
piped into its fuel system or flywheel ("Fuel in" ports).

- **Layout**: cylinders are grouped into **banks** by which way they point from the crank: one bank is inline (I4), two at right
  angles a V (V8), two opposite a boxer (B4), three a W (W12), four a radial (R16). Sockets are square to the part, so a V is
  90 degrees; a freeform attachment can set any angle.
- **Power** is the sum over the cylinders of the size's kW (40, 800, 16,000, 320,000) times the bank's best fuel system times
  its best booster. Torque is that power at the peak rpm (5000, 2800, 1400, 700; limiter 7000, 4000, 2000, 1000): big
  engines turn slowly. The curve is the petrol curve and the limiter is the soft limit. Fuel at full throttle is 0.00044 l/s
  a kW times the thirsts.
- **A cylinder's own power**: a cylinder may carry `enginePiece.power` and `thirst` (1 if not); the Engine Kit's tier 3 to 5 cylinders
  (Bored x1.3, Performance x1.6, Racing x2) do (docs/vehiclekit/ENGINEKIT.md). Its kW and burn are multiplied by them.
- **One fuel piece a bank** is enough, on any cylinder of the bank. A bank with none gives nothing and the readout says which.

### The readout

The look line on any piece, with any tool in hand, says what the pieces add up to and what is missing: "V8 320 kW, thirsty",
"V8 160 kW - no fuel system on bank 2", "no output: fit a flywheel on the end of the crank", "no starter: an engine this big
needs a starter motor", "no fuel: link a fuel tank", "mixed sizes: every piece of an engine is one size". Driving, the HUD's
red line gives the same reason. "thirsty" is 1.15 times the plain burn a kW or more, "frugal" 0.85 or less.

### How it is recognised

`ConstructEngine.Build` runs with the caches. Working pieces that touch (socket links, a join, a freeform attachment's bead) with the
same `MoveSeg` are one engine: a turntable between them breaks it, a freeform attachment doesn't. Bank directions are compared in
the rest frame (`ToRest`, `DirToRest`). Everything is derived from the parts and links, so a client works out the same engine
and no save or message carries it. Building allocates nothing past a construct's first piece. `Apply` puts the numbers in one
`ConstructPartDef` (`Construct.EngineDef`) and `ConstructDrive.Tick` runs that: no tick work per cylinder. The flywheel is the
construct's `Engine`, so wires, gearboxes, shafts, PTOs, anchored bases and seatless carts work as for a fixed engine. An
alternator touching any piece counts. The first engine that can run is the construct's, as with fixed engines.

### Damage

A cylinder under half its health **misfires** (half its power); wrecked it gives nothing and its piston stops. The engine runs
on what is left, rougher, smokier, with a misfire in the sound, until every cylinder is gone. A wrecked crank section cuts off
what is past it from the flywheel. A health change only reworks the sums when it crosses the misfire line
(`ConstructEngine.HealthChanging`, on the server and on a client's health update).

### The show

All on the rpm and load every peer has (the load is the top three bits of the power flags, 0 to 7, protocol 122).

- Boxes tagged `"pump"` (metres) slide, `"spin": true` turn about the part's z; the drawn rate is watchable, not the true rpm.
- Big engines rock at idle and smooth out as they rev; the pieces are drawn shaken, the body isn't moved.
- **Smoke**: grey puffs at idle, a black column under load, from the tops of the stacks the engine feeds, else its outlets,
  else one head a bank. A misfiring engine smokes more. Flames lick from the stack at full load. A fixed pool of 256 puffs,
  drawn in the blended pass.
- **Sound**: a loop of the firing pulses per cylinder count (1 to 16, with and without a dead cylinder), pitched by the rpm;
  the four nearest running engines are heard, louder with load.

### Decisions made (the developer may change them)

1. A V is 90 degrees: part rotations are square. A V at a true angle would need angled sockets.
2. Any one fuel piece feeds its whole bank, with no limit on how many cylinders.
3. Starters are needed from Large up, and need a battery. Small and Medium start themselves.
4. The stacks and outlets have no effect on the numbers: they are looks.
5. No overheating: the rev limiter is the only soft limit.
6. Only modular engines make the new sound; fixed engines stay silent as they were. `EngineSounds.Pick` is where to change it.
7. Capital pieces needed `MaxPartReach` 50 m and a 500 t mass limit. Pieces from Medium up have the size class's sockets (`sizeClass`):
   they bolt to that class's frames, gearboxes and bunkers, or to smaller ones through a size adapter.
8. Pieces of different sizes never mate, and forced on they say so.

## Tracks

Two kinds, one contact model. A **Track part** (Track, Short Track, Bogies and the Medium to Capital tracks) is a whole
track in one part on one suspension. A **freeform track** is a loop the player lays round wheels with the **track tool**,
like a belt on pulleys: triangle tracks, long lows, a raised sprocket, any wheel size. Code: `Source/Constructs/ConstructTracks.cs`
(rules, store, contact rays, tick), `ConstructEditsTracks.cs` (server), `Program.SandboxTracks.cs` (the tool),
`Source/Render/ConstructRenderTracks.cs`, `UiSandboxTrack.cs`.

### The track tool

On the tool wheel (the tenth tool, between the physics gun and the attach tool). LMB picks wheels one after another round the loop;
LMB on the first wheel or R closes it; RMB steps back; LMB on a picked wheel leaves it out; Delete drops the stroke. A green or
red band shows the track the picks make, and the look line says why not when it is red. The order doesn't change the shape: the
band pulls tight round the wheels' convex hull, so a wheel inside the hull is carried along but doesn't touch it.

With the tool out, on a track: R opens its settings (so does the configure tool's LMB on one of its wheels), Delete takes it off,
and LMB puts one that was shot off back on. Settings: **width** (starts across its wheels), **link length** (a share of the
smallest wheel), **tension** (how much the top runs sag: drawn only), **friction** (its grip, 1.05 like a Track part).

Rules (`ConstructTracks.Check`, the same on the client and the server): 2 to 48 wheels, all wheels and no Track part, none on
another track, all on one construct, stood up, turning the same way, in line within a wheel's width, on the body or all on
one steering pivot (no moving joints, no freeform attachments), at most 1,600 m round, 64 tracks a construct.

### Stored as

A track is **not** a routed link: `Construct.Tracks` (`CTrack`) beside `Construct.Routes`, holding its wheels (`TrackParts`), four
settings and its health. Links are point-to-point between ports, with a type's rules, a health per segment, spans of at most 8 m and
every system reading them by type; a track is a set of wheels. It goes through the same places routes do: `Remap` (parts
renumbered, a piece split off: a track goes with the piece only if all its wheels do), `Merge` (joins and joins), the full form
(so the patch form too: protocol 136), a blueprint's `"tracks"` (`{"wheels": [steps], "width", "link", "tension", "grip"}`, the
selector's copies and saved blueprints), and the end of a body in a world save (a body's bytes carry a length, so older saves
just end before it). A copy's tracks are whole. A wheel taken off leaves its loop; with one left, the track goes.

### The contact model

No link is a body. A track is a handful of contact rays, one speed and one drive (`ConstructPhysics.TracksDrive`, `TracksShare`).

- **The rays** are `CWheel`s, so springs, slew, grip, skid steering, decks and hub motors are the wheels' own code. Each wheel of a
  loop is one. Between them `BuildStations` adds rays along the loop's lower edge, by the run's length (spaced at least 0.6 wheel
  radii, at most 13), each on the nearest wheel's suspension. A wheel on no suspension still works: it is a raycast wheel on
  the track, with a little give of the track's own (`NoSpringHz`).
- **Lead rays** (`CWheel.Lead`) lie on the rising stretches: the curve round an end wheel, a raised front run, a raised sprocket.
  They touch nothing on the flat, carry no share of the weight (`Construct.LeadCount`) and hold no ride height, and a ray straight
  down from high over the band's edge sees a kerb before the body reaches it, so the nose rides up. A Track part gets four (two
  each end, `LeadRays`). What climbs is what the band's curve reaches; a step over the axle meets the body's solid parts.
- **One speed.** All of a loop's wheels share it: what the contacts make of the ground, or in the air a driven wheel's.
- **One drive.** Every driven wheel of a loop adds to the track's drive, touching or not, and the contacts share it by their grip,
  so a sprocket held off the ground drives the whole track. A raised sprocket counts as touching for the drive line.
- **A Track part is one wheel to the drive line.** While any of its rays touches, all count as touching, and those on the ground pass
  on the share of those that aren't (`TrackUnitsShare`), so one ray over a dip no longer reads as a wheel in the air on an open
  differential (which passes a tenth of the torque).
- **Skid turns.** While a track skid steers, its pads have `TurnGrip` (45%) of their grip sideways, so long tracks can swing (it comes back as the speed rises: an arc at speed, not a spin), and
  turning on the move the inner track is held back by `TurnBrake`, so the nose comes round instead of the outer side only
  speeding up. Hub-motor bodies keep `SkidSlack` as well.
- **Hills.** No collider: the wheel system changes once a wheel is on a track. On a grade a track's pads bite (`GradeGrip`: `BiteGrip`
  50% more grip at 45 degrees), it pulls low (`ClimbBoost`: up to twice the drive up a grade, fading out by `ClimbSpeed`, 6 m/s) and
  under throttle it doesn't slide back while the pads hold. Wheels get none of this. Dirt, a curved run-up, 20 s on full throttle: a
  Tracked Crawler holds 50 degrees (30 before), a 4x4 Truck 35, a Buggy under 30.
- **Climbing.** A wheel's ray past its travel only stops the body sinking; a track's lifts it (`ClimbRate`, up to half its slew), so a nose
  that meets a step higher than its spring goes up it instead of through it.
- **The band has a thickness.** A wheel of a track, and a Track part's rays, ride on the band: the ray reaches `CWheel.Band` (the link's
  thickness) past the radius, so the links lie on the ground and the wheels sit on them.
- **Parking.** With the throttle released a wheel stops dead under 1.5 m/s; a track runs down at `ParkDecel` (plus what a slope adds) and
  parks under `ParkSpeed` (0.25 m/s). Under throttle nothing holds it.
- **Slew.** Every track contact follows a step at `ConstructTracks.Slew` (2.4 m/s; a big suspension's `slew` wins). The contact climbs a
  kerb instead of taking it in one tick, which is what stops the hop on rough ground.
- **Drawing the ride.** A Track part lies on its rays (`UnitPose`: a line fitted through them, up its travel and tilted). A loop
  follows its wheels: suspensions spring and the band wraps where they are.

### Drawing

`ConstructRender.DrawTracks`, one instanced batch after the bodies: a plain band of chords round the loop, and links (boxes) sliding
round it at the track's speed (`CWheel.Spin` times the radius: each side its own, so they run opposite ways in a skid turn; a
client works it out from the body's motion at the wheel). A top run sags by its tension. Past about 700 link lengths from the
camera only the band is drawn, and past the body's far level nothing. At most 900 links a track. A Track part is drawn the same way
(its two ends are its loop), with its `"roll"` boxes as before, now turned by its own side's distance.

### Damage

A track has health (`FullHealth`: by width and length). Rounds hit its band (outer face and sides, `ConstructTracks.Ray`) and blasts
reach it; at zero it comes off: it is no longer drawn, its wheels run on as plain wheels (a wheel on no suspension is a solid part
again) and only those a drive line reaches are driven. The track tool puts it back on. No debris falls yet.

### Cost

Creative: free. Survival: **metal** from what you carry, then your beacon's network, the path ropes use: 1 unit a link (`ConstructTracks.LinkCount`:
the loop's length over its link length, rounded up). Making one pays for the whole loop, its Mirror Table twin too; mending pays for the share of the links
its lost health stands for, so all of them for one shot off; setting shorter links pays for the extra ones. Nothing comes back when one is taken off. A pasted
one is free (a clip's cost is its parts). The look line shows the cost; short of it the refusal says so (`Track: 34 metal - you reach 12`).

### Not done

- A track has no collider: players and other bodies pass through the band (the wheels are raycast wheels too).
- The drawn band answers the ground, not the other way round: near the camera, rays along a lower run (`TrackLift`, up to 33 a track, a
  frame) lift it over the ground, rocks and other constructs and let it droop a little into a dip. It is a look: nothing physical.
- Tension is a look only: a slack track doesn't throw.
- A track's paint: none yet.

## Big drive

What moves and powers big constructs, in the Medium, Large and Capital size classes (the small class has a Hub Motor, a
Genset and two thruster sizes). The target is a traction city: a tall town on giant tracks, about 100,000 t and 300 m,
with a mothership route in the air second. Data: `Constructs/Parts/BigDrive`, written by
`tools/constructs/big_drive_parts.py` (its tables at the top are the balance: run it from the repo root; `--check`
compares). Code: `Source/Constructs/ConstructPlant.cs` (plants, stacks, the pools), `ConstructThrust.cs` (hub motors,
thrusters, the hover hold), the wheels in `ConstructPhysics.cs`. Self-test: "Large drive".

**No water, no lift.** The game has no water surface and no aerodynamics: no ship screws, no wings. The only ways a big
construct moves are the ground and a thruster.

The **Traction City** blueprint is the worked example: two 128 by 64 m Chassis Decks, four Capital Bogies on Hub Motors (the
front pair on Steering Pivots), two Boilers with Chimneys and Bunkers, a Battery Bank, and a bridge seat on an adapter. It
weighs 84,000 t and is a base to build a town on.

### On the ground (the main route)

- **Hub Motor** (`"suspension": {"motor": {"torque", "power", "top"}}`): a suspension with a traction motor in it, driving
  its wheel or track from the circuit, with no shaft. So several bogies under one body are each powered, and a bogie on a
  **Steering Pivot** is simply powered too (its base bolts to a body's side or underside). The driver's W and S work it;
  a throttle wire (its data port `throttle`, -1 to 1) works it alone, with no seat. With no steered wheel, A and D drive the
  sides against each other (skid steering), and a track keeps 0.4% of its sideways grip while it does
  (`ConstructPhysics.SkidSlack`): no hub motor beats the whole weight's grip. It draws from the circuit: no power, no
  push. A track's rays share its motor.
- **Tracks and Bogies**: wheels with a `track` block (long Tracks 4 to 6 contact rays, short Bogies 2 or 3, each with four lead
  rays round its ends: [Tracks](#tracks)). Their boxes with `"roll": true` are the exposed drive wheels and rollers, which turn
  on the spot as the construct runs.
- **Gas struts** (`"suspension": {"ride": 0.33}`): every big suspension holds its height whatever it carries (up to
  `maxLoad`), half way down its travel; a plain spring on a 100,000 t body would ride at the top of it and lift rays off
  the ground. **`slew`** (m/s): how fast the contact may follow a step in the ground (what the body does itself isn't
  held back: that lag fed a bounce), so a big track rides up a kerb instead of taking it in one tick.
- **Shafts** still work at scale: Diesel, Gas Turbine, Steam Engine and Electric Motor in the three classes, a Gearbox,
  Reduction, Right-Angle and T Gearboxes, a Clutch, CV Joint, Wheel Hub, two Drive Shafts, and an open and a locked Axle.
  A capital diesel is 8 MN m at 102 rpm; through its gearbox (12:1) and a capital axle (8:1) that is 27 MN at a 24 m tyre.
  `maxTorque` now rates a gearbox and an axle as well as a transfer part: past it, the drive line slips (the note says so).
- **Rolling resistance** is a share of the load at g 18 m/s2, so a big part's `roll` is 0.004 to 0.008; a small tyre's 0.012
  would eat a whole capital engine. **Air drag** thins above 20 t (it goes with area, the mass with volume).
- **Legs**: Leg Pistons, Jacks and Knee Hinges work by wire like other joints; Landing Struts are spring feet. A solve can
  load one leg first, so a leg is rated for nearly the whole weight (see the limits below).

### Power plants and smoke

- **Plants** (role `generator`: `{"rate": kW, "fuel": "fuel" or "none", "burnRate", "spool", "buffer"}`): a Boiler, a
  Furnace, a Genset and a Reactor. A plant feeds the circuit it is on with no engine and no seat, follows what the circuit
  asks (nothing asked: no fuel, no smoke), and comes up over its `spool`; its `run` wire holds it off, or keeps its fire
  banked at 10%. A plant makes a construct electrical like a battery, and holds its `buffer` (kJ). Fuel reaches it as it
  reaches an engine; a Reactor needs none.
- **Flame** (`"flame": {"at", "dir", "radius", "length", "kind", "source", "colour"}`, or a list, any part): a flame drawn by data (docs/render/MATERIALS.md, "Flames by data"). `"at"` and `"dir"` are all it needs; a thruster or burner without one gets its own from its numbers. `"source"`: thrust, burner, engine, signal or always.
- **Material** (`"finish"`, a part or a box): the shared material it is made of, a row of `Materials/Surfaces.json`. Parts don't need it: the file's rules give every box one by its role, material, folder and colour.
- **Smoke** (`"smoke": {"at", "size"}`, any part): a stack puts out what a burning plant makes while one reaches it: the plant's
  own stack, a Chimney, Stack Section or Wide Funnel touching it (or touching a stack that does), or a pipe from its
  `Exhaust out` port to the stack's `Exhaust in`. An engine on fuel smokes as hard as it pulls. `Construct.SmokeLevel` goes to
  clients in the pose; the puffs are particles (`ConstructRender.EmitFlames`, `Fx`: nothing allocated). A burner's heat goes in the same share of the pose.
- **Steam engines** (`"curve": "steam"`): full torque from a standstill, falling away towards the limiter; their `spool` is
  raising steam.
- **Pools at scale.** A capital bunker holds 4 Ml and a bank 10 TJ, where a float can't take a tick's sip off: everything
  that burns or draws goes through `Construct.UseFuel` and `UseCharge`, which keep what doesn't register until it does.
  An engine's `starterKJ` is its own. Fuel Mains (`Fuel Main`, 7 cells) carry fuel where a link won't reach.

### Thrusters (the second route)

Role `rotor` is under [Propellers and rotors](flight.md#propellers-and-rotors), a `"surface"` block under [Wings](flight.md#wings).

Role `thruster`: `{"force": N, "fuel": "fuel" or "electric", "burnRate", "power": kW, "spool", "exhaust": a direction, "at"}`: a
force at a point, away from its exhaust, on a body that is free and awake. Five sizes of **Jet** and **Lift Fan**, 8 kN to
200 MN. It works from its own inputs: its `throttle` wire (0 to 1) sets it; its `height` wire holds that many metres above
the ground; unwired, the driver's W, S, A and D work it by how it is mounted (`CThruster.MixForward`, `MixYaw`), and Sprint
starts the **hover hold**: every thruster pushing up the body holds the construct level at a height (Sprint raises it,
Crouch lowers it, and sinking onto something lets go). The hold stays on with nobody at the controls, so a crew can leave
the bridge. Its levels start from a share of the weight trimmed so the pushes balance about the centre of mass
(`LiftTrim`), then each thruster holds its own point as fast as the slowest spool lets it. A construct with a lit thruster
has a little drag (speed and turn). A thruster on a turntable or a hinge vectors its push.
On a planetoid the hold works along the local up and climbs faster the longer Shift is held, and high up the air thins
(docs/space/SPACE.md, "Leaving the bubble"): past the sky's edge a flyer becomes a ship in orbit.

Twelve Capital Jets hold 124,000 t at 80% and burn 6,700 l/s, an hour of 24 Ml. Twelve Capital Lift Fans at that load draw about 11 GW.

### Network (protocol 122)

A pose's fuel is a float now (the old u16 topped out at 6,553 l), a pose and a full body gain thrust and smoke bytes, and
the power flags gain 16 (the hover hold; the engine's load has the top three bits).

### Limits to know

- One engine drives a drive line (the first working one); more engines don't add up. Several plants do, and hub motors
  are how a body gets many powered bogies.
- A seat, a lever or any small part on a capital hull goes through a size adapter (the Traction City's bridge seat does).
- A bogie on a deck's side rides at the deck's height. Under a deck it bolts on by a pivot's or a hub motor's `top` socket;
  its spin decides which way its leaf points.
- Small drive parts have no `maxTorque`: a capital engine reaching one through an adapter would go through it.
- Segment contact points (`MaxSegPoints`) are shared: when big parts on joints want more than fit, each keeps its corners
  (it used to be first come, first served: a jack's last foot had none). The load between legs still isn't shared evenly.
- No ground pressure: tracks don't sink.

### Choices made (the developer may change them)

1. Hub motors are the route for many powered bogies, not one engine and a tree of shafts.
2. A thruster's fuel comes as an engine's does (touching a tank, a fuel line or a pipe); it doesn't flow through the hull.
3. Boilers and furnaces burn the fuel pool: no coal from bins yet.
4. The hover holds a world height, not a height over the ground, unless its `height` wire is used.
5. A Reactor burns nothing and doesn't explode: its cost is only its mass (and what it takes to build).
6. Drive part mass, the table at the top of the generator: a capital diesel 2,400 t, a boiler 4,800 t, a bogie 2,000 t.

## All-wheel drive and round wheels

Wheels phase 1 (feat/wheels-1, 2026-11-11). The user's decisions: all-wheel drive for powerful generated vehicles, tyre settings the player
can change, round wheels, and wheel sizes from the Wheel Former where possible.

**Why.** Past about 1.7 times a V8's power, two driven tyres limit a vehicle, not its engine (docs/vehiclekit/ENGINEKIT.md "In a vehicle").

**All-wheel drive in the vehicle kit** (`Vehicle.Ladder`, `SteeredAxle`, `Powertrain`). An option, off by default, so every shipped seed
is made as before (`--gen-vehicles`: 238 identical). A generator sets `Vehicle.AllWheelDrive` before `Ladder`, or a wish asks for it
(`EngineWish.AllWheels`; on the command line `--gen-vehicle NAME --engine-tier N --awd`, named "..., all-wheel drive]").

- A differential stands in place of the front cross beam (the Wide Differential on a 0.5 m front wing, its wheels 4.5 cm further out;
  the plain one with no wing), its back face where the beam's was, so the rails and the engine's sill don't move. The front wheels stand
  12.5 cm ahead of the old axle line (`FrontAxleZ` says where).
- A CV Joint on each output, the Steering Pivot on its output, then the spring and wheel as before (the 4x4 Truck's way).
- The differential's input port (its `-z0` socket) faces forward and stays free, so only the right nose rail is fitted (at least 0.5 m),
  and a bumper goes on that one.
- A T Gearbox behind the gearbox splits the drive; its branch is a laid shaft under the rails, past the front differential and back into it.
- Not yet: a tandem (it stays two driven axles), a front wing other than 0 or 0.5 m (refused, the spawner rerolls).

Measured (`--speed-test`, tier 5 kit engine asking 36 m/s, Bandit Technical seeds 1, 6, 13): two-wheel drive 25.5, 29.2, 30.2 m/s, all
grip-limited; all-wheel drive 34.2, 35.2, 32.5 m/s, limited by power against drag; 0-20 m/s in 2.6, 1.8, 1.8 s (was 6.7, 4.2, 4.0).
Each passed the spawn drive check. Seed 6's tyres set by blueprint to a dry grip of 0.4 g and 1.6 g (two-wheel drive): 15.7 and 37.3 m/s.

**Tyre settings** were already there ("The tyre's settings" above): dry, dirt and side grip, rolling drag and bounciness.

**Round wheels.** The hand-made wheels (`Constructs/Parts/Wheel.json`, `Drivetrain/Wheel*.json`) draw a 24-sided cylinder over their
three solid boxes, which are hidden and still collide as before (players, bullets, the physics gun, a loose wheel); sockets, ids and size
are unchanged. A sprung wheel already meets the world as a cylinder ("Wheels: the rim and the cylinder"), so the drawing now matches the
contact. Wheel Former wheels and the Dually Wheel were already round; the BigDrive tyres and drive wheels come from
`tools/constructs/big_drive_parts.py` and are still three boxes.

**Next (phase 2).** Round BigDrive wheels in their generator (or its C# port); all-wheel drive on a tandem and on other front wings
(drive shafts between the differential and the CV joints); a faction or tier choosing it; the kit choosing a Wheel Former wheel by its key
(diameter, width, material) instead of a named wheel, which needs an off-road `wheels` row.
