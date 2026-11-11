# Power and links

Covers links between constructs and power, routed links, world links. Part of the construct guide: index in ../CONSTRUCTS.md.

## Links between constructs and power (milestone 3)

Board item dev-sandbox-m3, part 3 of 4 (the developer approved it 2026-10-29). Code: `Source/Constructs/ConstructLinks.cs`
(couplings, wires between constructs), `Source/Constructs/ConstructPower.cs` (the electrical system), the coupling rows
and joint reactions in `ConstructPhysics.cs`.

### Couplings: hitches and chains

- A **hitch** part (`"role": "hitch"`, a `"hitch"` block: `kind` `ball`, `eye` or `hook`, `at` the coupling point in
  the part's frame, and for a hook its `chain` in metres) couples to a hitch on **another** construct: a ball or a hook
  to an eye. Shipped: **Tow Hitch** (a tow ball on a crossbar), **Tow Eye** (a drawbar's eye), **Chain Hook** (2.5 m of
  chain) and **Wrecking Ball** (450 kg with an eye on top).
- **Coupling and uncoupling**: with the **attach tool** out, F on a hitch couples it to the nearest free hitch that fits (a
  ball's point within 0.6 m of an eye's; a hook's eye within its chain and 0.3 m) or uncouples it. Bare hands or another
  tool: "take out the attach tool (3) to couple or uncouple". A coupling also goes when either hitch is **shot off** or
  wrecked, when its parts end up on one construct, or when the two are torn more than 1.5 m apart (a teleport, a blast).
- **A tow ball is a free pivot**: three rows in the contact solve (one per axis) hold the two coupling points together,
  pushing either way (no friction), so the trailer turns any way about the ball and can't come off it. **A chain** is one
  row along the line between the points that only pulls (the ball swings and drops, but never further than the chain).
  Each side moves its body and every joint up its hitch's chain, like a contact, so a chain hook on a turret's arm works.
  Coupled constructs still collide everywhere except within 0.7 m of the hitch points.
- A towed construct (coupled to one that is driving) lets go of its **parking brake**; coupled constructs wake each
  other when one moves. Couplings name their parts by a stable tag (`CPart.Tag`), so they survive other parts coming off
  either side (salvage, a split).
- The chain is drawn as links along a curve that sags when it is slack; a tow ball shows a pin.

### Ropes and springs

Free constraints between any two points, tied with the **attach tool** (the old attach tool; its code name is still `ConstructTool.Welder`, byte unchanged): its ring has **Rope** and **Spring** at the two ends, with Socket attach, the default, straight out from the tool. The attach tool joins pieces; the linking tool links what is already placed. Code: `Source/Constructs/ConstructConnectors.cs` (the list, the tick, the solver rows), `ConstructEditsConnectors.cs` (the server's checks), `Source/App/Program.SandboxConnectors.cs` (the tool), `Source/Render/ConnectorRender.cs`, `Source/UI/UiSandboxConnector.cs`.

- **Using it.** LMB picks the first point on any construct, wreck, loose body or the world (terrain, map geometry); walk if you like (up to 120 m); LMB picks the second. A ghost line runs to the crosshair, green, or red with the reason, and the HUD line gives its length, size and survival cost. RMB or Esc cancels. With nothing held, RMB at a line takes it off. R steps the size: by the parts at its ends, S, M, L, XL. The **configure tool** on a line opens its panel (length; strength or stiffness and damping); the **remover**'s LMB deletes one at once.
- **Rope.** Its ends stay within its length (new: the gap + 2% + 5 cm); below that it is slack and doesn't push. A one-sided row in the contact solve, the coupling chain's row, warm started, each side moving its body and every joint up its part's chain. Over its rating (smoothed: a quarter of each tick's pull, so one jerk counts but one tick's noise doesn't) it **snaps**: both halves whip back to their ends, with a crack, fibres and a word to the players near. Nothing else damages it. A rope has no weight, doesn't touch anything along its length, and isn't shot through.
- **Spring.** Pushes and pulls to a rest length (new: the gap) with a stiffness (N/m) and a damping (N s/m; new: 30% of critical for what it held when fitted). An implicit soft row, `ConstructFlex.FlexPrep`'s sum on two points, so no stiffness can blow it up. **Stiffness 0 is a plain damper** (it draws as two tubes): no kind of its own. A spring doesn't snap.
- **Sizes.** S, M, L, XL: rope 1.5, 6, 25, 120 t; spring 15, 60, 250, 1200 kN/m. By default the smaller end part's biggest side picks it (up to 0.6, 1.5, 4 m). The panel can set a rope weaker, or in creative up to 20 times stronger. The size itself is fixed once tied.
- **Cost.** Creative: free. Survival: from what you carry, then your beacon's network, no Build Station: **wood** for a rope, 1 unit a metre times 1, 2, 4, 8 by size, rounded up; **metal** for a spring, 2 a metre of its rest length, times the same. Short of it, the refusal says so as a look line (`Rope: 12 wood - you reach 5`). Lengthening a rope in the panel pays for the new metres. Nothing comes back when one is taken off: a rope is used up. A pasted one is free (a clip's cost is its parts).
- **Storage.** One list on the world (`ConstructWorld.Connectors`, 1024 at most, grown as used), whether its ends are on two bodies, one body across a joint, or a body and the world. It is never a segment. Ends name their parts by `CPart.Tag` with the point in the part's own frame, so a rope survives other parts coming off, splits, joins and moves. It goes when an end's part goes or is wrecked (a tie made to a part already wrecked stays), when it is pulled 6 m past its length, or when the ground under a world end goes (one end a tick is checked). Both points on one rigid piece is refused (a joint must lie between them). World saves: section `Connectors`. Blueprints and selector copies: `"connectors"`, when both ends are in the selection; world ends stay behind.
- **Wire.** `CBodyKind.Connectors`: the whole list (a joiner, after the bodies; a big change), or a patch of those made or changed (renumbered parts) and the ids gone; u16 part indices. `CActKind.Connector` new, delete, set. `CFireKind.ConnectorSnap` carries both ends. Clients draw from the list; the pull isn't sent, so taut or slack is worked out from the distance.
- **Waking.** A taut rope or any spring wakes a sleeping body when the other end moves (a body, or an end carried by a joint), and both ends are simulated together in a big world. A sleeping or anchored body is a fixed point to the row.

**Tool or parts.** Both exist. The tool gives the free constraints: any two points, any length, no sockets, no 40-odd part files by size and length, and it reaches the world; its costs are that it is not a thing you can place in a catalogue, attach, shoot or see the mass of. Dedicated parts (`feat/linear-joints`: sliders, electric and shaft actuators, rails, and Pistons) are for guided and powered motion, which needs a socket, an axis, a segment and a motor. A rope or spring gives none of those: it is a row between two bodies, so it can't guide anything along a line, and powered travel stays with the parts.

### Hydraulics

`Source/Constructs/ConstructHydraulics.cs`, the ram row in `ConstructConnectors.cs`, parts from `tools/constructs/hydraulics_parts.py` (`Constructs/Parts/Hydraulics`).

- **Cylinder.** The attach tool's fifth ring option. Like the Spring: click two points; R cycles the size (electric S to XL, then hydraulic S to XL). A `ConnectorKind.Cylinder` pinned on ball ends, with a capped two-way row in the contact solve, so it drives hinges as a real linkage (the rope's one-body path across a joint). Its A end stands on a tool-made **head** part (`toolOnly`: never in the inventory or the Assemblers), freeform-welded at the first point, which carries the ports: that gives it hoses, health and a place in blueprints, saves and joins with no new format. `Stiffness` holds its shortest length, `Damping` its longest, `Rating` its most force, `Length` what it is told to be, `RowForce` the force this tick (0: limp, no row). Cost: 4 times a rope's a metre, through `BulkPay`.
- **Electro-Hydraulic Cylinder.** The electric heads: self-contained, weak and slow. Power and a Set signal (0 to 1 of the stroke), nothing else.
- **Circuits.** No fluid simulation. A Hydraulic Pump, turned by a shaft or on power with none linked, charges a circuit of pipe links (decision: the pipe type with fluid oil, no new link type). `Solve` runs at 6 Hz (`Hz`) per construct: each pump has one pressure and one flow, each head traces pump, valve and hose (`Trace`). Force is pressure times area, capped by the head; speed is the head's share of the flow over its area, so ten cylinders on one pump go a tenth of the speed. A circuit stays on one construct: world links aren't traced. Nothing is held in the pipes: a shut valve holds what it holds with the pump stopped.
- **Cut hose.** The head goes `HoseCut`: `RowForce` 0, limp, and it sprays (a fire event). The wrench or attach tool repairs the hose as any link.
- **Engine load.** Work on the rods comes off the engine (`Construct.HydLoad`, in `ConstructDrillDrive.Carry` with the drills and rails), as displacement times the pressure the work asks; a head that can't move asks the full relief pressure. An engine held under 30% of that load for 2 s stalls, dead for 3 s (`StepStall`, and `DriveOn` is off meanwhile). A gearbox in the shaft trades speed for torque. A small pump doesn't stall the 260 N m Engine; a Medium pump against a stop stalls it and the V8.
- **Valves.** A valve's signal, -1 to 1, is how far it is open and which way. Lever Valve: the Analog Lever's handle, sprung to the middle, metered by how far you push; stack them side by side for a bank (each is one section). Digital Valve Block: a `scale` logic part driven by data, from a seat's Input Reader or any logic.
- **Cost of nothing.** A construct with no hydraulics has empty lists: `StepHydraulics` skips it, and `Solve` allocates nothing.

### Cable alignment

Three things keep links tidy.

- **Beside.** The auto-router's lattice has planes one link-width off every existing run (`RunBox`: the two radii and `LoomGap`), and a run there costs half (`BesideFactor`, against 0.8 along a surface), so a new link lies beside an old one when that isn't much further. Tray lanes get the same discount.
- **The Loom.** A mode on the linking tool's ring. LMB picks links on one construct (2 to 12; LMB again drops one), R goes on to the run, LMB lays its points (each square to the last; Free routing lets it go any way), R lays them; RMB steps back. `ConstructRoutes.LoomPlanFor` runs on the client for the preview and on the server for the edit (`CActKind.Loom`): the links lie across the run one link-width apart, in the order their ends come so they don't cross, every bend mitred so the spacing holds round it. Each link joins the run from its own end, straight when the path rule passes, otherwise square; a link whose ends sit the other way round runs the bundle backwards. All of them pass the one path rule, or none changes. A shaft can't be bundled, and a link with branches off it is refused. New segments are at full health, as with Re-route.
- **Cable Trays and Clamps** (`ConstructTrays.cs`, a part's `tray` block). A node set on one snaps to its nearest lane, the auto-router prefers lanes, and `ConstructRoutes.Damage` takes off up to 80% (`Shield`) by how much of the segment lies in a guard. A clamp is short but guards a metre of run. A construct with none costs nothing.

### Wires between constructs

- The linking tool now wires **into another construct**: fire on an output, then on an input of any construct within
  **12 m** (part centre to part centre). The same rules as a wire inside one construct ("those are already wired", a
  part's input limit counts both kinds), and "an input reader reads a seat of its own construct". Aim on a part cuts
  its wires of both kinds. A wire between constructs carries its source's value from **last tick**, like every wire.
- It **breaks** when either part is gone or wrecked, or when the parts are more than 12 m apart (a towed trailer driven
  off, a wall button's post carried away). Drawn with the linking tool out, orange while off.
- A map can wire its starting constructs (below): the yard's **Wall Button** runs the **Motor Gate**.

### Power

- A construct with a **Battery** part (`"battery": {"capacity": kJ}`; it comes charged, 300 kJ) is **electrical**: its
  electrical parts work only while a **working battery's links reach them** (the frame is the circuit: a wrecked part
  breaks it, so shooting the battery or the beam between cuts the power) and the batteries have charge. Electrical
  parts: every logic part (with no power they put out 0), every moving joint's motor except steering pivots (a motor
  with no power lets go: a lift sinks, a motor gate hangs loose), and anything with a `"power"` draw.
- **The draw** (watts, `"power"` in a part file): a part draws while its signal is on (lamps 60 W, horns 120 W, other
  logic parts 2 W); a motor draws 10 W while it moves plus its work (torque times speed) over 0.8. `Construct.PowerUse`
  is last tick's total. A flat battery comes back on at 0.2 kJ (no flicker at empty).
- **The engine needs the starter**: asked to go (the driver's throttle, or a signal into it) an electrical construct's
  engine takes 2.5 kJ to start (the engine must be on the battery's circuit); flat: "It won't start: the battery is flat".
  It runs until nobody asks it to. An **Alternator** touching the engine charges the batteries while it runs (1.5 kW).
- **Optional per map** (decision): a construct without a battery runs as milestone 2 did (nothing needs power), so every
  earlier blueprint keeps working; a map's `"power": "required"` makes every construct electrical (no battery: no logic,
  no motors, no start). The Wired Buggy (battery, alternator, charge gauge), the Motor Gate (battery, charge gauge) and
  the Lamp Post carry batteries; the Buggy, Gate, Lift and Turret don't.
- The **HUD** while driving shows the gear and the charge ("charge 84%", "(flat)", "engine off"); the look line shows a
  battery's charge and "(no power)" on an electrical part without it.
- Construct guns don't draw power (yet): a gun part could, by giving it a `"power"` draw and checking
  `ConstructPower.On(construct, part)`.

### Joint reactions

A joint's motion pushes back on the body it is mounted on: what the joint gained this tick (its speed against the last
tick's, in momentum: `_jVel0`, `_jReacted`), whatever gave it, the body takes back as a turn about the joint's axis or a
piston's push at its pivot. A turret swung hard rocks a light buggy; a turntable under a block hanging on a chain turns
the block the other way; a slider that hits its stop kicks the body. Steering pivots don't (they are the driver's hands
on a rack). `ConstructPhysics.JointReactions` turns it off (the self-test compares).

It is the motion, not the motor's push. A motor that only holds an arm up gains no motion, so it reacts nothing: that
weight is on the body already. Three details keep the books right for joints in series:

- The reaction goes through the whole body's inertia (`_invI`), because that is the body the joint's solve leaves
  unchanged. An arm heavier than the base it stands on stays stable; the base's inertia alone gave it momentum from nothing.
- A body's mass and centre are those of its rest pose, so `ArmWeight` adds the turn from each jointed piece's weight
  being somewhere else now, through what holds the body up last tick (`held`: ground, wheels, thrust; nothing in free fall).
- A body the physics gun holds takes neither: the beam carries the arm's weight and what swings on it, so the body isn't kicked against the beam's aim every tick.
- What a joint carries is posed joint by joint (`Construct.PoseCarried`, `SegNow`), so an elbow's bend moves the shoulder's load.
- What a joint turns has the inertia of its spread as well as its centre's swing (`SegSpin`, rest shape), so a beam centred on a
  turntable is as heavy to turn as it looks.

### Gears

A transmission may have `"transmission": {"gears": [ratios], "labels": [...], "gear": n}`. Since the drivetrain families
a ratio is a **reduction** (the engine turns that many times for each turn out, the lowest gear first) and the engine has
a torque curve and a limiter ([Drivetrain parts](drivetrain-and-engines.md#drivetrain-parts-the-families)); the shipped Transmission has three
(4.4, 3.0, 2.1; it starts in 2). The linking tool's R picks its gear; a signal wired in (one input) picks it instead
(1 the first gear, rounded; below 0.5 the R gear). The **Gear Selector** (logic kind `selector`) steps 1, 2, 3, 1... on
F or a rising input (an input reader on the driver's reload key makes a gear shift).

### More parts

**Bumper** (rubber-faced bar, armour 10, 500 health), **Fender** (a mudguard panel), **Speed Gauge**, **Fuel Gauge** and
**Charge Gauge** (logic sensors with a **dial**: `"dial": {"at", "face", "max"}` draws a needle swept 270 degrees by the
signal; the fuel and charge gauges put out 0 to 1, in hundredths), the **Gear Selector**, Battery, Alternator and the
hitches above. 12 new part files; 50 in all.

### The map's links and the yard

The `"sandbox"` section takes `"power": "optional" | "required"`, `"couplings": [{"hitch": [construct, step], "eye":
[construct, step]}]` (the second construct is moved so its eye meets the ball) and `"wires": [{"from": [construct,
step], "to": [construct, step]}]`, constructs by their index in `"constructs"` and parts by blueprint step. Construct
Yard adds a **Trailer** coupled to the Buggy (which now has a Tow Hitch), a **Wall Button** at (-6, -14) wired into the
Motor Gate's toggle, and a battery **Lamp Post** at (-13, -5) that lights while someone is within 6 m. (They are listed
after the weapons' constructs, so the map's couplings and wires name constructs 12, 13 and 14 and the Motor Gate, 3.)

### Self-test and pictures

The **Construct links and power** section (`Source/App/SelfTestConstructLinks.cs`): 16 bad hitch, battery, gauge, gear
and sandbox-section files refused for the right reason; the buggy tows the trailer 19 m in 3 s and round a 90 degree
turn with the coupling points within 1 cm; bare hands refused, the attach tool uncouples (the buggy drives off alone) and
couples again, the hitch shot off; a wrecking ball swings on its chain and never gets further than 2.5 m from the hook;
a wall button wired into the Motor Gate opens it, the wire's refusals, and the wire breaks when the button is carried
out of range; a flat battery stops the Motor Gate's motor, the wired buggy's lamps and its starter, a little charge
starts it and the alternator charges it, a shot battery cuts the power, and a map that requires power stops a buggy with
no battery; a turntable under a block hanging on the chain turns the block back (not with reactions off); gear 1 pulls
harder and tops out at 11.4 m/s while gear 3 goes faster, and a gear selector picks gears 1, 2, 3; the yard's trailer,
wall button wire and lamp post; a client's copy of the links, the charge and the gear, a truncated links packet
refused; no allocation in 120 ticks of all of it. "Constructs"' loopback also checks a client and a late joiner mirror
the couplings, the wires between constructs and the power.

### Choices made (the developer may change them)

1. Power is **optional per construct**: no battery means milestone 2's free power, unless the map says `"required"`.
2. The **frame is the circuit** (no wire parts): power reaches whatever a working battery's links reach.
3. Steering pivots and the engine's drive don't use the battery (only the starter does); a running engine charges only
   through an alternator touching it.
4. Wires between constructs reach **12 m**; couplings carry no power or signals.
5. Coupling is **instant** with the attach tool (F), not timed; a rigid coupling made up to 0.6 m apart pulls together.
6. A body's mass, centre and inertia use the rest pose, with `ArmWeight` for the weight's moment; so an arm reached out tips
   the body truly, while the body still turns about its rest centre and with its rest inertia.
7. Gears: three in the shipped transmission, the second as before; no reverse gear (S still reverses).

## Routed links (decision 20)

Board item dev-sandbox-routed-links-rl02 (the developer's design, 2026-10-30). Code: `Source/Constructs/ConstructRoutes.cs`
(the links, their rules, flow, damage, the wire format), `ConstructRoutesBuild.cs` (blueprints' `"links"`, the migration),
`ConstructEditsRoutes.cs` (the server's edits), `Source/App/Program.SandboxLinks.cs` (the linking tool on this PC),
`Source/UI/SandboxLinkWheel.cs` (the linking tool's options on the tool wheel's outer ring), `Source/Render/RouteRender.cs` (drawing them).

One routed-link system on the linking tool replaces the invisible wires and the Fuel Line part. A link is a polyline of
**nodes** from an **output** to an **input** on one construct, routed node by node over its surface, drawn by one
segment piece and one elbow piece per type (instanced), and simulated as a graph edge (never a physics body).

| Type | Look | Routing rule | Carries | Segment health |
| --- | --- | --- | --- | --- |
| Data | small flat cable | free | on/off and number signals (the logic's wires) | 20 |
| Power | larger flat cable, red stripe | free | electricity from a battery or alternator to anything that draws it | 40 |
| Pipe | round, a flange ring at each node, one neutral look | bends in 15 degree steps | fluids from tanks to engines | 80 |
| Drive shaft | round steel, a U joint at each bend | straight, at most 45 degrees a bend (and into a port), no corners (a gearbox does that), no bare junctions (a T gearbox does that), never across a joint (a U or CV joint device does that), except its end into a suspension's Drive in ([half shafts](drivetrain-and-engines.md#half-shafts)) | torque from engines, gearboxes, transfer parts and differentials, port to port, and into a Drill Module's Drive in | 250 |
| Chute | square duct with rims, a bellows across a moving joint ([Flexible chutes](joints-and-logic.md#flexible-chutes)) | bends in 45 degree steps | items between storage parts (a load of bulk or one stored part a second); a part may name its own chute ports (a Storage Volume's Chute in and Chute out, a Drill Module's Chute out, which takes nothing in) | 80 |

**One pipe** (decision 28): there is a single pipe and it carries any fluid (fuel, water, oil, steam...). There are no pipe
styles, no per-style radius or ratings (every pipe is 5 cm in radius) and no wrong-pipe failures: nothing weeps, burns,
bursts or makes an engine sputter because of what runs through it. It is drawn one neutral way, round with a flange ring
at each end of a section, so at every node (its colour is for the paint tool to change). Pipes still carry what the
source tank holds to the engines it feeds; a cut pipe still pours fuel out. Blueprints and saves from before the change
may still say `"style": "fuel"` (or water, oil, steam) on a pipe link: it is read and ignored, and the link loads as the
one pipe. Blueprints no longer write it. On the wire the type byte of a link is the type alone (protocol 98); the high
nibble that held the style is masked off when read.

**Routing** (the linking tool): Fire on a device (a picker of ALL its ports, every type, by name: "Fuel in", "Drive out"; the port picked gives the link's type; a one-port device gets a one-option picker too; [Link type from the port](flight.md#link-type-from-the-port-at01)); each
Fire places a node on the surface under the crosshair: off the surface by the link's radius, on the construct's 5 cm grid
along the surface, onto the straight line on from the last segment when close to it, lined up with a neighbouring link's
nodes, then turned to the type's angle rule. The preview shows the link so far and the next segment, red with the reason
when a rule is broken (too close, a node sinking into a part, a segment through a solid part, the bend, a span over
8 m between nodes). A link has no limit on its length: only each span has one (user, 2026-11-04). What is left is the
storage for nodes, 8,192 a construct (`MaxRouteNodes`, 65 km of link at full spans), and 8,192 in the world for world links. Fire on an input finishes it, by the route the game found ([Auto-routing](#routed-links-decision-20), below; a picker when it has several free ports, with the port nearest the crosshair highlighted so one more click confirms); Aim or Backspace takes
the last node off; Esc cancels. A straight data cable may still go into **another** construct's input (milestone 3's wire
between constructs). The server checks the whole link again (`ConstructRoutes.Check`). The player must be in reach of the input, the end clicked last,
and no longer of the output: a long link is laid by walking along it, placing nodes (through the Build Gantry's camera, its range counts as reach, for
laying a link and for the edit modes). The preview checks only the last segments as the crosshair moves (`PathRule`'s `checkFrom`), since the rest was
checked as it was laid; the server's check of the whole link runs once. Nothing in a link is read per tick by its length, and `ConstructRoutes.Ray` skips
16 segments at a time on one test when they are too far from the ray to touch it (`CRoute.RayRun`).

**Clipping grace and embedding** (board item dev-sandbox-link-clip-grace-lg01; `ConstructRoutes.PathRule`, the one rule
the server's `Check` and the client's preview both run). Along the first and last stretch of a link nothing clips it:
for `Grace(radius)` = 2 x the link's diameter + 10 cm (`GraceDiameters`, `GraceExtra`) from where it leaves its output
(or from its junction) and up to where it enters its input, nodes and segments ignore every part (the rail or frame a
device sits on too). The grace counts from where the link leaves the device's own boxes (`ExitAlong`; a port at a part's
centre would otherwise use it up inside the part), and the device itself stays exempt on its end segment as before.
Past the grace a link may sink up to `EmbedFraction` (60%) of its thickness into a part: its centreline may be
(2 x 0.6 - 1) = 0.2 x radius inside a surface (`EmbedDepth`). Nodes use a signed distance to the boxes
(`SignedDistance`, `NodeSinks`); segments test the centreline against each box shrunk by that depth (`SegmentBlocked`),
which is what lets a link turn round the outside of a corner. Nodes are still snapped onto the surface (decision 20).

**Hold to adjust** (lg01): when the preview is red, LMB doesn't drop the node: it freezes at the click and the adjust
mode starts. The game first searches along the surface normal (out 1 cm at a time up to 50 cm, then in down to the
embedding limit: `AdjustSearch`) and jumps to the nearest valid offset; while LMB is held the wheel moves it along that
normal (1 cm a notch, Ctrl 5 cm; not Shift, which is free routing) and the preview re-checks every step, red with the
reason or green. Letting go on green places the node; on red nothing is placed (RMB cancels too). The offset shows in cm
beside the crosshair, and the wheel neither swaps weapons nor turns the wheels meanwhile. The same works for the input
end (the last node before the input moves along the normal it was placed on, then the link finishes) and for the edit
mode's move node, whose preview is now checked with the server's rule (`CheckMove`) and goes red too. The node rules are
reusable `INodeRule`s (`OpenRule`, `ClosedRule`, `MoveRule`); nothing new goes on the wire.

**Free routing** (board item dev-sandbox-link-shift-free-ls01; `ConstructRoutes.ClickEnds`): while the **Free routing**
key is held (Left Shift by default, rebindable; it shares the key with Sprint, see `Keybinds.Overlap`), aiming at any
device places an ordinary node on its surface (the normal snap, grid, angle, grace and embedding rules) and never ends
the link, opens the port picker or branches, so a link can run along a device with a matching port or beside another
link. The device's highlight is hidden while it is held; the HUD line says "Left Shift: free routing (no port snap)".
Let go of it to end at an input. Before the first click it does nothing special. It keeps working while adjusting a
node, and a moved node may sit on a device either way. The server needs nothing new: a node on a device is a legal node.

**Auto-routing** (`Source/Constructs/ConstructAutoRoute.cs`, `RouteSearch`; `Source/App/Program.SandboxAutoRoute.cs`, the tool). With
an output picked, aiming at an input shows the whole route as a live ghost: blue while it is searched for, green when it fits,
red with the reason. One click confirms it (an input with one free port); an input with several ports opens the picker as
before, its ghost follows the port the pointer is on and a click there confirms. A click on a surface still drops a node, and
the router carries on from that node, so nodes clicked on the way are waypoints; aimed at a surface the straight line can't reach,
the ghost routes to that spot, and the click takes the route with the spot as its last node. Hold the Free routing key for the old
manual flow: straight segments only, no search. Hold to adjust is unchanged, and acts when no route was found. The look line says where it
stands in the usual form: `Pipe to Engine: route found, 5 nodes, 6.2 m - LMB: confirm`,
`Pipe to Engine: no route - no way round the parts - LMB a surface: place a node`.

- **The straight line comes first.** A straight link that fits is used as it is when it is a shaft's, under 75 cm, or square with the
  construct's axes. A longer diagonal goes to the router for a tidier square route, and comes back as the straight line
  only if no square route exists.
- **Cables, pipes and chutes** search a lattice of box faces: the planes one link radius off each box, the two ends' planes and a stub off
  them (a Hanan grid: the shortest square route round axis-aligned boxes lies on it), thinned to at least 5 cm apart. Edges a box
  touches are marked in a grid, which is what keeps A* cheap: looking up an edge costs one array read. A* runs over (point, direction),
  charging 0.4 m a bend, 0.8 x the length along a surface or beside another link's run, and 2.5 x along another link's run. All
  turns are 90 degrees, which suit the pipe's 15 and the chute's 45 degree steps, and a run is at least 10 cm between
  bends (1.5 x the radius for a chute). A link leaves and arrives along a port's face (sideways costs 60 cm, backwards never), and the first and last
  stretch may pass through what the clipping grace forgives. Four stages widen the look: 1 m round the ends, 4 m, 12 m,
  then 12 m on a 2 cm lattice with the clearance dropped to nothing, centreline on the surface, which `EmbedDepth` allows; that is for slots narrower than a link.
- **A flood from the goal** first marks every lattice point that can reach it, whatever the bends, so a shut-in goal costs one pass of
  the lattice, and A* stays out of pockets with no way on.
- **Shafts** can't turn 90 degrees, so they search an even lattice (10 cm, growing with the distance) in the 18 directions a 45 degree
  limit can step between, finishing as soon as a straight run reaches the port, or a stub off it. More than 4 bends is refused
  (`ShaftMaxBends`), and the reason names what is needed: `no gentle shaft run: fit a right-angle gearbox at the corner` when the
  ports are more than 80 degrees apart, else `no clear shaft run: line the ports up, or fit a gearbox or a U joint on the way`.
- **Joints and constructs.** A shaft across a joint gets the old refusal at once. Other links may cross: nodes go on the output's side until
  a node is nearer the input's side, so a chute crosses once, in one flexible section. Off the construct, a data cable into
  another construct and world links behave as they did: the router only runs on one construct.
- **Nodes** are ordinary nodes: each goes on the nearest part on its side of the joint, in the path's frame (`Hosts`). Pieces freeform-attached at an
  angle count as their bounds. The client checks the result with `ConstructRoutes.Check`, and the server checks it as it checks a manual
  link: nothing new is trusted and nothing new is sent. It searches at most 24 m at a time (`RouteSearch.MaxReach`): further than that the look line asks
  for a node on the way, and it carries on from there.
- **Cost.** It runs on the client, 2 ms a frame (`BudgetMs`), and nothing allocates after its first use makes its tables (about 19 MB, once). A search restarts
  when the construct is rebuilt (`RebuildStamp` is in the key), and 8 results are kept per link and goal, so sweeping between ports searches nothing twice.
  Caps: 1,048,576 lattice points, 262,144 states, 200,000 states searched per stage. Waypoints wait for the crosshair
  to rest for 150 ms. Measured on the 2-core box under load, on random 30% full block hulls (shared ports, searches up to 24 m):
  a 2,000-part hull found routes in a median of 1 to 4 frames (shafts 12 to 16) and a 7,990-part hull in 2 to 8, worst 254 frames (4 s) for
  a failing search; the final `Check` takes 0.02 ms on 7,990 parts, because `Blocked` and `NodeSinks` now ask the part tree when it can answer.
- **Limits.** Routes are square, so a pipe or chute tail left at an odd angle by a manual node may have no legal way on (the look line says so:
  take the node off or carry on by hand). Links aren't obstacles to each other, only a cost. A search that gives up says so, and manual nodes
  still work.

**Junctions**: Fire on a link of the type picked starts a branch at that point (a junction node goes into the link).
Power, fluids and items split evenly at a junction (each way gets an equal share of what arrives: the look-at line shows
the share; a chute's branches take turns); data copies the signal to every branch. Shafts don't branch.

**Attachment**: every node sits in the rest frame of the part it is on, so links on jointed parts move with them; a cable
or pipe crossing a joint flexes there. Links go with their parts when a construct splits or is joined, and with the
blueprint.

**Destruction and repair**: bullets, construct guns and blasts hit link segments like parts (each on its own). A cut
segment stops the flow beyond it: a cut fuel pipe pours fuel out (0.5 l/s) and a blast through it lights the tank, a cut
power cable sparks. The the attach tool (Fire, nothing in hand) mends a link; the edit ring's re-route starts it
again from its output.

**Edit modes** (options on the tool wheel's outer ring round the linking tool): delete a link (and its branches), move a node, re-route a link, highlight
all (every link bright through the parts). Data and power cables are dimmer unless the linking tool is out.

**Migration**: logic wires are data links routed straight (a blueprint's `"wires"` still work); when a blueprint is built,
each chain of Fuel Line parts between a tank and an engine becomes a fuel pipe along the same path, and each chain of
plain drive shafts between two drive parts a shaft link from port to port (the shipped Buggy, Wired Buggy and Hood Gun
Buggy now carry their tank on the deck with a fuel pipe; the 4x4 Truck's five shafts became two shaft links). A chain
whose removal would drop a part (a tank hung only on its line) keeps the old parts, which still work. The Fuel Line and
the plain drive shafts are `"legacy": true`: not in the parts menu or on a rack. U and CV joints, gearboxes, T boxes,
transfer cases, clutches, chain and belt drives (they have ratios), power take-offs, hubs and differentials stay devices.

**Blueprints**: `"links": [{"type": "pipe", "style": "fuel", "from": step, "to": step, "nodes": [[x, y, z], ...]}]`, with
`"fromPort"` / `"toPort"` (socket names) for shafts and `"branch": [link, segment], "at": [x, y, z]` in place of `"from"`
for a branch; nodes in the construct's frame, each on the part nearest it.

### Named link ports (np01)

Board item dev-link-named-ports-np01 (the developer, 2026-10-30 / 31). A part file declares the ports its links use **by
function**, with a top-level `"ports"` list (`Source/Constructs/ConstructPorts.cs`): `name` ("Fuel in", "Drive out",
"Input shaft"), `type` (`shaft`, `pipe`, `power`, `chute`; data ports stay the logic part's `DataIn` / `DataOut`), `dir`
(`in`, `out`, `both`), and where it is: a shaft port names its `socket` (that socket's index is still the link's port byte, so
the drive solver and the wire format are unchanged) and may move its point (`at`) and facing (`face`) to the middle of a
face; a pipe or power port gives its own `at` + `face`. A pipe port may name its `fluid` (fuel, coolant, exhaust, oil,
water; none: any) and any port may say `"noEffect": true` (listed in the picker as "(no effect yet)": the exhaust and coolant ports,
a battery's Power in; the simulation has no exhaust or cooling system yet and does nothing with them, and a
tank's pipe into an engine's Coolant in does not feed it). A fuel tank's Fill takes fuel from a Fluid Tank ([Fluid tanks](#fluid-tanks)). The first port of a type on a part is port byte 255, the next ones
`0x80 | k` (shafts use the socket index). Every part that draws power gets an implicit "Power in".

- **What a link starts and ends at.** `ConstructRoutes.IsOutput` / `IsInput` for shafts, pipes and power cables are "the part
  declares a port of that type that can start (end) a link"; a part without one is red with "the X has no shaft port"
  (structural parts never offer shaft ports). A link starts and ends exactly at the declared point of its port
  (`PortPoint` / `PortDir`): a differential's "Input shaft" is the middle of its -Z face, its "Output left" / "Output right"
  the middle of the side faces; each port has a small flange or stub drawn on the face. A shaft leaves a port straight out.
- **The picker** lists only the ports of the link's type that can take it and are free, by name, with a hint: in / out, the
  fluid, "(no effect yet)". A pipe carries what its output port produces, so ending one on an engine lists just the inlets
  that want that fluid ("Fuel in"); with a single candidate it connects with no picker. While aiming at a part, its matching
  ports show as small labelled markers where they sit.
- **Fluid warning.** An input port that wants another fluid than the pipe carries (exhaust into a fuel inlet) is allowed but
  the link's look line says "WARNING: exhaust into a fuel inlet" (`ConstructRoutes.FluidWarning`, `EndNote`).
- **Old saves.** A blueprint link whose `fromPort` / `toPort` is a socket's name (the old directional ports: "+z", "-z0")
  loads onto the nearest named port of the same type that fits (`TryPortByName`, `NearestPort`); with none the link is
  dropped and logged ("a drive shaft at the X's old port ... dropped"). The old drive-shaft parts migrate the same way.
  Blueprints now write the port's name. Protocol 103.
- **Checks.** `--check-construct-parts` prints every part's ports and flags a part with a linking role missing the ports it
  needs (`ConstructParts.PortProblems`); the loader refuses a port whose point is not on the part's surface. Self-test:
  **Named link ports** (`SelfTestNamedPorts.cs`).

### Fluid tanks

Six sizes in `Constructs/Parts/FluidTanks` (written by `Source/PartKit/Generators/FluidTankParts.cs`, the fuel tanks' bodies and
capacities): Small Fluid Tank (15 l), Fluid Tank (40 l), Large Fluid Tank (150 l), and Medium, Large and Capital Fluid Bunker
(20 kl, 1 Ml, 4 Ml). Role `fluidtank`, code in `Source/Constructs/ConstructFluid.cs`.

**Fluids.** The game's list is `enum Fluid`: fuel, water, oil, steam, coolant, exhaust, and for [lift cells](flight.md#air-and-lift) hydrogen, helium, hot air and vacuum (no pipe carries the last two). Engines, power plants and thrusters burn fuel. Water, oil, steam, coolant and exhaust are carried and stored, and no part makes or consumes them yet (the
exhaust and coolant ports stay "(no effect yet)"). Refining and assembling work in bulk materials over chutes, not fluids.

**Two stores.** Fuel tanks share one pool per construct (`Construct.Fuel`), and pipes between them only say what reaches what.
A Fluid Tank is a store of its own: one fluid and a fill level, as two part settings (`fluid`, `fill`), so it moves with the
part and rides in blueprints, world saves and joiners' CBody like any setting. The two are joined at their pipes:

- **Direction.** A pipe runs from an output to an input as it was laid: each tank has a **Fluid out** and a **Fluid in**, and
  a pipe moves fluid only that way. Two tanks that should trade both ways take two pipes.
- **Flow.** Each tick a pipe with a Fluid Tank at either end moves what fits, up to the narrower tank's `flow` (a pool's end
  doesn't limit it) times its share at junctions, in tank to tank, pool to tank (a fuel tank's Fuel out) and tank to pool (a
  fuel tank's Fill). A cut pipe pours a Fluid Tank's contents out at 0.5 l/s, whatever they are.
- **Empty.** A tank with no fluid or at 0% is empty, takes whatever arrives first, and goes back to no fluid when it runs dry.
- **Mismatch.** A pipe bringing another fluid than the tank holds moves nothing and takes nothing out of the source. The link
  stays. The look line says so at both ends (`Large Fluid Tank: 15 of 150 l oil - refused: a pipe brings water, this holds oil`,
  `Fluid Tank: 20 of 40 l water - refused: the Large Fluid Tank holds oil`) and on the pipe (`WARNING: water refused, the tank
  holds oil`). Water piped into an engine's Fuel in is the same: `refused: the Engine burns fuel`.
- **Burning.** A Fluid Tank feeds a burner it touches or that a pipe from it reaches a fuel inlet of, as a fuel tank does.
  Burners draw on the pool first, then on the feeding tanks that hold fuel (`Construct.FuelLeft`, `ConstructFluid.Burn`); the
  gauge and the driver's HUD count both. A tank of water reaches the engine, and the engine has no fuel.
- **Between constructs.** A world pipe works as a pipe does, at the same flow rule. A fluid hose first moves the pools as
  before, then from the first tank with something in it to the first that has room for it (`ConstructFluid.MoveAny`).
- **Wrecked.** What it held is lost, and it stops working until mended. A Fluid Tank holding fuel, hydrogen, oxidiser or oil goes
  up when it is wrecked ("Fuel blasts"). Taken off, it goes back to the inventory empty, as a part loses its settings.

**Creative only.** The configure tool sets the fluid and the fill in creative worlds. Both are `"creative": true` settings: in
survival the panel shows them read-only (Reset, Paste and Apply to selected go too), and the server refuses a request that
names one (`ConstructEdits.Configure`), whatever the client sends. A survival world building a blueprint, or pasting a copy,
leaves those values out, so its tanks arrive empty: neither can bring in what the configure tool can't set. Moving a selection
keeps them. A survival tank fills from pipes.

**The network.** A client's copy follows the server's: `CBodyKind.Fluid` (u16 body, u16 part, u8 fluid, f32 fill each), five
times a second for the constructs whose tanks changed, ordered and reliable (protocol 138). The fill is a percentage in a
float, so what a tick's sip can't register on a big tank waits in `CPart.FluidOwed` (not saved: a fraction of a litre).

**Choices made (the developer may change them):** two pipes for two ways rather than one both-way port; a mismatch refuses at
the flow, not when the pipe is laid, since what a tank holds changes; a fill set with no fluid chosen holds nothing; picking a
fluid at 0% doesn't reserve the tank; the survival blueprint rule above; no fire; no liquid weight; a Fluid Tank's fluid isn't
drawn on the part; a Bunker of fuel has no Fill, so a pipe can't fill it (a world pipe, a hose or a dock still can).

### Tank Volume

One block (`Constructs/Parts/FluidTanks/TankVolume.json`, written by `Source/PartKit/Generators/FluidTankParts.cs`; wrench, 14 kg, 400 health) with a box of space you size yourself: a Fluid Tank in every other way (role `fluidtank`, `"fluidTank": {"volume": true, "flow": 5000}`), so everything in [Fluid tanks](#fluid-tanks) holds: one fluid, Fluid in and Fluid out on the block, mismatches, burners, world pipes, hoses, creative `fluid` and `fill`.

- **Sizing: the Storage Volume's own tool.** The box is the same six settings (`sizeX/Y/Z` 0.2 to 40 m, `atX/Y/Z` up to 40 m from the block; 2 m cube above the block by default), so `ConstructCargo.HasBox` gives it the configure tool's face handles, R's corner-to-corner drawing, the panel, the reach to the box and blueprints with no new code or wire. While you drag, the look line says what that box would hold.
- **Capacity.** 1,000 l a cubic metre of the box as sized (`ConstructFluid.Capacity`): 8 l to 64 Ml, 8 kl by default, less what its open faces cost ([Sealed volumes](#sealed-volumes)).
- **Resizing.** What it holds stays: the fill percentage changes, not the litres. A box too small for that is refused and undone ("Empty it first"), and growing into another Tank or Storage Volume on its segment is refused. Survival may size the box; only `fluid` and `fill` are creative.
- **In the world.** The outline, floor marks and handles are the Storage Volume's, in blue. The fluid is a see-through box in its fluid's colour (`ConstructFluid.Colour`) rising along the block's up, with a brighter surface so the level reads. It isn't drawn while its box is being dragged.
- **Not solid.** Box and fluid collide with nothing. Only a player's look ray meets the fluid, so the look line and the configure tool's Fire work at the level. An empty box has only its block to aim at.
- **Mass.** The block weighs 14 kg, whatever the box's size. What it holds weighs too, through a bin's path (`Construct.LoadOf`, the 4 Hz load step, at the fluid's centre): kg a litre fuel 0.8, oil 0.9, water and coolant 1, steam and exhaust 0.01 (64 Ml of water is 64,000 t). A plain Fluid Tank's contents still weigh nothing.
- **Limits.** 4 Tank Volumes a construct. Flow 5,000 l/s at most, whatever the box's size. The attach tool refuses to take off one that holds anything ("empty the Tank Volume first"); the remover still deletes it, contents and all. Part replace refuses it, as for other volumes. Wrecked, it loses what it holds.
- **Survival.** It costs what its part costs (3 steel, 1 rubber), whatever the box's size. A blueprint or paste keeps the box and arrives empty.

**Choices made (the developer may change them):** the block's cost and flow don't grow with the box; contents weigh here but not in plain Fluid Tanks, which are small and would change existing vehicles; one block, no size classes.

### Sealed volumes

A Tank Volume or Storage Volume is meant to sit inside walls. Code: `Source/Constructs/ConstructSeal.cs`; numbers: `Constructs/Sealing.json`.

- **The check.** Each of the box's six faces is tested when the construct changes: a grid of points on the face (one a metre, 3 to 6 a side), each covered if a solid box of the construct's own lies across it within 0.35 m either way. 90% covered seals the face. The result is a 6-bit mask, `CPart.SealedFaces`, worked out in `Construct.Rebuild` (a part added, removed or shot off) and again when a non-steering joint has moved, on the server and each client alike, so nothing goes on the wire and nothing is saved. Terrain doesn't seal: only the construct's own solids.
- **The cost.** The mask is read at the bins' 4 Hz. A construct with no such volume costs one check. One check took 0.15 ms at 126 parts, and it allocates nothing.
- **A box being sized** waits half a second after each change before it can pour (`Construct.SealWait`), so dragging or typing costs nothing.
- **The look line** says what is open: `Tank Volume: 3 of 8 kl water - 2 faces open: holds 83%`. With a tool out, open faces are outlined in red.

| Penalty | Tank Volume | Storage Volume |
| --- | --- | --- |
| Capacity, all six faces open (each face a sixth of it) | -50% | -10% |
| Pour rate, of the box a second | 4%, at least 20 l/s | 0.4%, at least 10 units/s |
| Level over an open side's lowest point before it pours, of the box's height | 2% | 25% |
| Acceleration ignored below, and how much of the rest counts | 2 m/s2, all | 5 m/s2, 60% |
| Fire eats, of the box a second | 2% | 0.5% |
| What can catch | fuel, oil | wood, planks, cloth, rubber |

1. **Capacity.** Over its new limit, the rest pours out.
2. **Tilt.** Down is apparent gravity. The level is the fill's share of the box's height along it; if the lowest corner of an open face is under the level, it pours. So an open bottom empties a box, an open side drains a tank at rest, and an open top only matters when tipped or braked.
3. **Acceleration.** The same check with the body's acceleration taken off gravity (`Construct.Accel`), so hard braking throws contents out and a parked base is safe.
4. **Where it goes.** Off the open face's lowest corner, clear of the rim. Fluid goes into a tank under it (a part, or a Tank Volume's box), else it is lost with a spill splash. Bulk goes as a tipped scoop's does (`ConstructScoops.PourVolume`): into a store under it, heaped ground for dirt, stone and sand, or Material Bundles, in lumps of 40 or more. Stored parts stay put.
5. **Fire.** An incendiary round or flame landing in or at an open box of something that burns lights its block (`ConstructSeal.IgniteAt`, `ConstructWorld.CanBurn`). The fire then eats the contents and lasts while they do; a Tank Volume holding fuel goes up when it is wrecked, burning or not ("Fuel blasts"). A plain Storage Volume's block doesn't otherwise burn.

**Old builds.** The mask needs no data, so saves and blueprints load unchanged and every box is checked on load. An open box over its new limit starts to pour within a quarter of a second of the world starting, and an open side or bottom drains what stands above it.

**Cargo Volumes** aren't included: a logic part that freezes bodies, with no contents to lose.

**Choices made (the developer may change them):** six equal faces; contents in a face's 10% gap don't leak; parts in a Storage Volume aren't spilled, though the capacity limit stops more going in; a volume's pour goes into its own construct's stores if one is under it.

## World links (board item wl01)

A routed link may leave its construct: a base can be laid out on the ground with pipes, cables and chutes running across
the terrain between constructs. The code is `Source/Constructs/WorldLinks.cs` (the list and its tick),
`ConstructRoutesWorld.cs` (the rules), `ConstructEditsWorldLinks.cs` (the server's checks), `Game/GameSandboxWorldLinks.cs`
(actions, the network, hints) and `App/Program.SandboxWorldLinks.cs` (the linking tool).

- **What it is.** A server-owned list beside the constructs (`ConstructWorld.WorldLinks`, at most 64, 1024 nodes). Each
  end is a construct's part (named by its tag, resolved each tick like the wires between constructs); each node is either
  on the world (a world position on the terrain or static map geometry: map boxes, buildings, props) or on a construct's
  part (the rest frame of that part's segment, like an ordinary link's node).
- **Routing.** With the linking tool, start a link at an output as usual; the first click off the construct (on the ground,
  a wall or another construct) makes it a world link. Each LMB places a node on whatever is under the crosshair, snapped
  off the surface by the link's radius (on the ground, the height is found again under the snapped spot), on the 5 cm
  grid and to the type's angle rule (`ConstructRoutes.WorldSnap`, sharing `AngleSnap` with the construct links). A red
  node moves to the nearest valid spot along its surface normal when there is one (the shared `AdjustSearch` with
  `WorldOpenRule`). LMB on a matching input of any construct finishes it; RMB or Backspace takes the last node off; Esc
  cancels. A data cable clicked straight into another construct's input with no nodes is still milestone 3's straight
  wire. The delete mode deletes a world link; its look line names its ends.
- **The rules** (`ConstructRoutes.WorldPathRule`, one rule for the server and the preview): the construct links' span
  limit (8 m), angle rules, clipping grace at the ends and embedding forgiveness, checked against every
  construct near the path (in its rest frame) and against the static world by rays (a segment lifted by the embedding
  depth must not hit anything; a world node must not sink deeper than it). A world node must sit on a surface (within its
  radius plus 15 cm). The end must be in reach of the player; the start may be any distance behind.
- **What flows.** Fuel from a tank into another construct's tank (2 l/s, while it has room); charge from a battery or
  alternator into another construct's batteries (5 kW); signals (the logic reads the source's value like a wire between
  constructs); items from storage to storage (a chute's move a second). Anchored constructs count as the world.
- **Moving constructs.** A link with any point on a free (not anchored) construct snaps when that point moves more than
  5 cm or its construct turns more than 3 degrees since the link was made (`SnapMetres`, `SnapDegrees`): it is removed,
  the end sparks (a power cable) or spills (a pipe) (`CFireKind.LinkSnap`), and the players within 25 m get the hint
  "the power cable to the vehicle tore off, sparking: it moved". Nothing is damaged. Making a link doesn't wake a free
  construct (a parked buggy woken on flat ground creeps about 5 cm before it sleeps again, which would snap it at once).
- **Ground going.** Each tick one world node checks its surface is still there (a ray the way it found it when made); if
  it is gone the link breaks ("the ground under it is gone"). Blasts now dig craters (decision 16,
  docs/sandbox/TERRAIN-DIGGING.md); the self-test stands in a world that loses its ground.

### Hoses (temporary connections)

Six parts in `Constructs/Parts/Hoses` (role `"hose"`, a `"hose"` block: `end` outlet or inlet, `carries` fluid,
power or data, `at` where the hose leaves or plugs in, and on an outlet `length` and `flow`): the Fluid Hose Reel (12 m,
2 l/s), Power Lead Reel (10 m, 6 kW) and Data Lead Reel (8 m), and their inlets (Fluid Hose Inlet, Power Lead Socket, Data
Lead Socket). The fluid hose replaced the fuel and water hoses (decision 28); the old names "Fuel Hose Reel", "Water Hose
Reel" and the two inlets still load as the fluid hose (`ConstructParts.Renamed`), and `"carries": "fuel"` or `"water"` in a
part file reads as `"fluid"`. The inventory lists them under Joints, "Hoses and leads".

- F with empty hands (any tool) on a reel takes the receiver (a nozzle or plug); it is held while the hose is out, not a
  backpack item, and counts as full hands for `ConstructEdits.Give`. F on a matching inlet of another construct within
  the hose's length plugs it in; F on the plugged inlet takes it out into the hands; F on its reel hangs it back; the
  Drop key lets go (it reels back), as does dying or leaving.
- It connects any two constructs, moving or not: pulled past its length (a vehicle driving off) a plugged hose pops out,
  and a held one slips from the hands 25 cm past it; it reels back, nothing is damaged, and the players near get a hint.
- What flows goes from the reel's construct into the inlet's at the reel's rated flow: fluid (between tanks that hold the same
  fluid: fuel into fuel tanks, water into water tanks), charge into the batteries, and a data lead's signal (wired into the reel, it comes out of
  the socket).
- A hose is drawn as a sagging rope (a parabola from its slack, 12 thin boxes) from the reel to the receiver in the
  holder's hands or the inlet; nothing is simulated or collides. The look line on a reel or an inlet says what F does
  there; holding one, the HUD says how much hose is out.

### Network and state (protocol 95)

`CBodyKind.WorldLinks` carries the whole list (each link: type, ends' construct ids and parts, nodes: construct id, 0
for the world, part, position; each hose off its reel: the reel, the holder, the inlet) whenever it changes, and to a
joiner. `CAct.WorldRoute` and `CAct.WorldRouteEdit` (delete) are at the end of `CActKind`; hoses use `CAct.Use` on their
parts (the server sees the role) and `CAct.Drop`. `CFireKind.LinkSnap` draws a snap's sparks or spill.

**What's missing:** world saving doesn't exist yet, so world links and hoses live as long as the match (they round-trip
through the join snapshot); blueprints don't carry them (they are between constructs). World links take no damage yet
(construct links do), and they carry whatever the tanks hold (fuel or water) through the same pipe.

### Self-test

"World links" (`SelfTestWorldLinks.cs`): a pipe across the ground between two pinned tanks carries 2 l of fuel in a second (and the same pipe with the tanks holding water, 2 l of water),
and a sunk node, a wrong bend and a shaft are refused; a data cable across the ground lights a lamp on another construct,
and a node in the air is refused; a power cable from a buggy still settling to a pinned battery stays while it comes to
rest, charges it, and snaps within a tick of the buggy driving off with the hint and sparks; the ground going breaks a
link; a fluid hose from a station refuels the buggy at its rated flow, unplugs and hangs back by hand, and pops out at its
length when the buggy drives off, unhurt; the wire round trip; no allocation per tick; a real host and a separate client
on the Construct Yard: the client routes a world link and plugs a hose over the network, and it and a late joiner mirror them.

### Choices made (the developer may change them)

- A drive shaft can't be a world link: torque stays on one body (two bodies' drive lines would need joining).
- A world pipe ends at a tank, not an engine: the engine runs from its own tank (one fuel pool per construct).
- The hold-to-adjust mode is for construct links; a red world node jumps to the nearest valid spot along its normal.
- Links keep the construct links' limit (8 m a span, no limit overall); hoses pick up with E only (not LMB).
- Hoses go from the reel's construct to the inlet's (a station refuels, a base charges); a reel is a station's or a
  vehicle's alike.
