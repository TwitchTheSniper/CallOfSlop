# Self-tests and history

Covers the construct self-tests, milestone records and decisions. Part of the construct guide: index in ../CONSTRUCTS.md.

## Self-tests

The **Flexible joints** section (`Source/App/SelfTestFlexJoint.cs`): the parts and the joint cap; a beam on a rubber joint droops where load / stiffness says, a wrecking ball takes it to its stop past its rating and off again it comes back with little overshoot; softer settings droop further, a signal locks it; kicked every way it stays inside the circle and a ball joint twists; a wrecking ball on four parts at both ends of their stiffness, kicked at random, stays finite, in one piece and settles, and a free 30 kg block carrying one stays sane; a block on the leaf by the attach tool, socket attachments on and off the grid and a freeform attachment hangs and swings alike; a motor hinge beyond it works, a wheel beyond it is a plain part; a player stands on the arm with both movers and bends it; a chute over a moving motor hinge, piston, turntable and ball joint carries bulk, one over a hinge carries parts; a piston past a section's reach stops it with the reason and it carries on coming back; a turntable stops one at 135 degrees, tears it past that with a hint, and the attach tool mends it; a cargo hose moves a load a second and pops out at its length unhurt; nothing allocates. **Flexible joints network**: a real host, a client with lag and loss and a late joiner see the same joints, axes, angles and strain, a torn chute, and a cargo hose the client plugged in carrying loads.

The **Structural parts** section (`Source/App/SelfTestStructureParts.cs`): every file in `Constructs/Parts/Structure`
loads (none refused anywhere) and keeps the cell rules; each one goes onto a 0.25 m cube by every socket on all six
faces in all four spins (every part reaches all 24 turns; about 53,000 placements), its origin always on a whole
centimetre and its cells on the cube's lattice; every socket takes a copy of the same part, which joins on; each shaped
box turns exactly with its part in all 24 turns; armour against the real guns' damage (armour plate stops every SMG,
rifle, LMG and buckshot round and pistols up to 40, lets marksman and sniper rounds through; heavy armour stops every
small arm up to 68 and lets bigger sniper rounds through; plate steel stops buckshot, not rifles; sheet steel and glass
stop nothing; the mounted MG is stopped by armour plate, the autocannon, cannon and rockets get through heavy armour);
the materials ladder (glass to heavy armour) stops more at each step; a round down through the Gun Port Panel's slit goes
through while one beside it hits the plate; the fitting checks and blocked hits allocate nothing; and the inventory lists
every structural part under its group and section, with the old blocks after the new beam sections.

The **Construct weapons** section (`Source/App/SelfTestConstructWeapons.cs`, milestone 3): the weapon part files load
(eight guns, the five heavy ones among them, the gun parts, the boxes) and ten kinds of bad file are refused for the
right reason; a rifle gun built part by part with the tools (the attach tool refused on a gun part; its problem goes from "no
barrel" to "no feed" to none as the barrel and magazine go on; 600 rpm, a 30-round magazine, AP penetrating, the scope
tightening it; the parts weigh what their files say; a switch wired in by the linking tool fires 10 shots a second from a
magazine loaded out of the box); the Hood Gun Buggy's gun points with the buggy as it turns and its driver's fire key
fires it (and stops when let go); the Gunner Turret follows its gunner's look three ways round and its fire key fires it,
the lever turning it again when the seat is empty; the Sentry draws no fire (and doesn't turn) for a player beyond its
sensor's range, turns onto and fires at one in range, and stops when they go; an ammo box feeds its gun through a feed
chute, 25 rounds fire exactly 25 shots, and a shot-off chute leaves the gun with no box; a gun's rounds hole an ammo box
which cooks off a beat later and hurts a block a metre away; ball rounds stop at a wood panel, AP rounds go through it into
the block behind (and only that), HE rounds splash a block off the line, incendiary rounds set the wood alight but not the
steel and it burns down on its own; in an offline game the local player in the yard's Gunner Turret turns it with their
look (their view held) and shoots a player 9 m out (credited), and the yard's sentry shoots a player who walks up but not
one beyond its range; no allocation in 120 ticks of a sentry and a gunner's turret firing (aiming, loading, the fire and
ammo messages written); and a real host and client: the client draws every round the server's gun bench fires from its
own copy of the gun, its ammo matches, and a late joiner gets the rounds.

The **Construct merged receivers** section (`Source/App/SelfTestConstructMerge.cs`): the rules file loads and six kinds of
bad file are refused; the diagram's shapes (T, L and 1x4 stay single; 2x2, 3x3 and 4x4 merge; a 3x4 is a 3x3 and three
singles; two 2x2s side by side; turned and heavy squares merge, mixed types and a turned member don't); finding a 4x4's
merges 200 times allocates nothing; built with the tools, the fourth receiver of a 2x2 pops the barrel, magazine and box
off the first one's own sockets (the barrel into the hands, the rest into the backpack), the members' own sockets refuse gun parts,
the centre sockets refuse an ammo box and show ghosts; a barrel and magazine on the centre set are 2x (twice as long, four
times the weight; the ammo box as it is), the gun 4x the damage, half the fire rate, 4x the magazine and recoil, firing 4
rounds a shot out of its load and box; a client's copy and a copy rebuilt from the parts find the same merge, scales and
gun; five more receivers make a 3x3 with the barrel and magazine moved on at 3x (9x the damage, 9 a shot); the attach tool on
the bottom-right corner takes only that receiver (a 2x2 and four singles left, the centre parts back at 2x); then the
bottom row's second: the 2x2 above takes over and the barrel follows onto its centre front; then the middle: no square
left, the centre parts pop off; a 2x2 has its members' health together, and a cell shot to pieces breaks it and drops its
centre barrel as a loose part.

The **Construct joints** section (`Source/App/SelfTestConstructJoints.cs`): a flap on a passive hinge with its axis flat,
latched open and flung down at 6 rad/s onto the yard's terrain, stops on it (no part of it below the ground at any tick),
then sleeps and stays put; a free arm (a beam, a motor hinge, a beam) driven into the floor lifts its own construct more
than 0.3 m and never goes into the floor; the Motor Gate's door opening into a post stalls at it, stays stalled and
awake, and goes on to its limit when the post is taken away; the Turret's arm turned into another (anchored) construct
stops at it; the Gate's door swung at 10 and 25 rad/s at a 5 cm wall never gets through it; the yard's six jointed
constructs all sleep after 3 s alone; no allocation (a stalled joint, and a dozen jointed constructs moving at once) and
the dozen inside 2 ms a tick. Most of it on a flat test floor with box walls (`JointTestWorld`).

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

The **Construct salvage** section (`Source/App/SelfTestConstructSalvage.cs`, milestone 3): the recycling yields and the
map's `infiniteParts`, `debrisSeconds` and `debrisNear` (bad values refused); in an offline Sandbox game on the yard a
wheel that fell off the buggy as a wreck is salvaged (timed) into the inventory with the
hand left empty, a lone wrecked suspension is not salvaged on site (it is hauled to a Recycler), someone else's working gate is refused until a part of it is
wrecked and then salvaged; with infinite parts off the parts menu refuses a part the inventory lacks, hands one out of
it, a fit uses it and the next comes into the hand, the player's own gate's door goes back into the inventory, a held
counted part goes back on respawn, and the HUD shows the counts; with them on the player's own parts come off into the
hand; a wreck left alone goes after the map's time, one being salvaged or with a player near stays; the Sandbox tick,
the debris check and the inventory allocate nothing; and a real host with two clients: the salvaging client sees its
count (the other doesn't), a client's gate is owned by that client on every copy, and an expired wreck goes on the
clients too.

The **Sandbox parts menu** section (`Source/App/SelfTestSandboxPartsMenu.cs`): the map's `partsMenu` flag reads (on by
default, a bad value refused); in an offline Sandbox game the menu's request hands the chosen part by id to the player
anywhere, an unknown id is refused (the hands kept), the rack still refuses from afar and hands out its next kind at the
rack, a Turret spawns in front of the player and a bad blueprint index is refused, and the map flag turns both off; the
window draws headless (every part listed, a search narrows it, a pick puts the part in hand with its tool out and closes
the window) with no ImGui error and no allocation per frame while open; a real client's part by id and blueprint go over
the wire; and outside Sandbox a blueprint spawn is refused. The inventory's tabs and tree headless: every tree node (each
group, each section) lists exactly its own parts, a search narrows within a node ("lamp" in Logic finds the Lamp, in
Structure nothing), the Blueprints tab lists every blueprint and taking the highlighted one ("buggy") builds it, the Tools
tab lists every tool model and taking the first one puts it in hand; steady open frames allocate nothing with "inf" badges
and with counts from a source.

The **Sandbox inventory** section (`Source/App/SelfTestSandboxInventory.cs`), headless: the catalogue holds every part,
blueprint and tool exactly once, today's parts all have a named group (none in Other) and the five main groups exist, an
unknown role goes to Other; every tile slot gets boxes (a blueprint built as the server builds it) and a camera that keeps
every box corner inside its square while filling at least 75% of it; and every slot has its own place in its atlas page.


The **Sandbox tool wheel** section (`Source/App/SelfTestSandboxToolWheel.cs`): the direction-to-segment maths (every
segment's middle and both edges for 8, 4, 3, 6 and 1 segments, the wrap-around at the top, the dead zone, a pointer that
isn't a number), the pointer held at its reach so one flick turns it; the key (an unshared middle mouse button; binds
version 9 files get it, one that already used the button keeps it); in an offline Sandbox game a scripted
hold-flick-release takes out the attach tool (east), physics gun (north-north-east), linking tool (west-north-west) and hands (south), the current tool is
marked when it opens, letting go in the dead zone cancels, the scanner says "coming later" and changes nothing, the
north-east takes out the physics gun, a menu and
death close it without choosing (and the still-held key doesn't reopen it), and it doesn't open in a seat; 240 headless
frames with the pointer going round (the linking tool's outer ring among them) draw without an ImGui error and allocate
nothing; and a real client's pick reaches the server.

The **Sandbox tool wheel outer ring** section (`Source/App/SelfTestSandboxWheelRing.cs`): the outer ring's arcs (every
option's middle and edges for 13, 2, 3, 9, 1 and 10 options, centred on the tool, the far side of a partial arc picks
nothing, the arc or full circle choice); the linking tool's options are the modes only (route, delete, move node, re-route) and the highlight toggle, with no link type and no arc; in an offline Sandbox game pointing at
the linking tool shows its ring with all nine, animating in; flicking out onto each option and letting go takes the linking tool out with it; letting go on the
tool keeps the last option (marked on the ring), and in the middle cancels; an attach tool given two test modes picks from its
arc, and outside the arc the pointer turns the inner ring; and every tool's and option's name, the title and an over-long
name fit the centre circle at UI scales 0.75 to 2 (shrunk, or cut short only for the over-long one). The third tier (a test set with a five item arc on the attach tool slot, since the pipe lost its styles): five items span 125 degrees centred on
the option; resting on it opens the arc with nothing picked; entry and keep distances (no flicker); an item calls `ChooseSub`,
letting go on the option calls `Choose`; moving to the next option or inward closes the arc; the "Sandbox tool wheel" section
draws it with a sub-option hovered and checks it allocates nothing.

The **Sandbox physics gun** section (`Source/App/SelfTestSandboxPhysGun.cs`): the model, the wheel slot and the tool
byte; in a bare construct world on the yard a grabbed block is pulled onto the beam's point (2 m up) and held in the
air, the wheel's distance moves it 3 m out, a snapped turn is reached, the snap maths (steps of 45 degrees, a snapped
turn stays put); a frozen block doesn't move in 2 s against gravity; with a second player's block frozen too, player
0's unfreeze-all frees only theirs (which then falls); a second player can't take a held block; grabbing a frozen one
unfreezes it; a construct with someone unfriendly aboard, a pinned one and one too heavy for survival are refused; the frozen state
round-trips through the structure; survival's contested rule refuses the physics gun, the attach tool and the linking tool
("Owned by Rival - defended") and allows them in creative, with the owner away, on nobody's and on your own; nothing
allocates over 30 ticks (unused, and holding); and over a real loopback a client grabs, steers and freezes a loose part
on the server, the host can't take it, the host's own beam (its held construct and grab point) reaches the client, and
the client's unfreeze-all frees it.

The **Sandbox tool models** section (`Source/App/SelfTestSandboxToolModels.cs`): every tool file loads as one piece with
its hands in reach, every tool in the game and every tool still to come has a model, and 13 kinds of bad file are refused
for the right reason (two models for one tool among them); in first person each model draws its boxes (plus sparks and a
beam in use) with both hands on their grips, low on the right of the view, and the equip drops the old tool before the
new one comes up and hands back to the gun after the drop; in an offline Sandbox game a bot's attach tool (attaching: its
sparks), a carried wheel and the local player's linking tool are drawn in third person instead of the gun, the scene graph
matching the immediate code draw for draw, and a gun again outside Sandbox; 240 frames of tools in use allocate nothing;
and a real host and client each draw the other's tool in their hands.

## Decisions made for milestone 1 (open questions it touches; the developer may change them)

1. **Joints**: only the passive hinge ships, as a reduced-coordinate angle on its body; the joint record is ready for a
   motor (turntable) and a slide (piston), which milestone 2 adds with the linking tool and logic parts.
2. **What players start with**: a gun loadout as usual, both tools always available (keys 3 and 4), and an endless
   parts rack on the map. No resources, crafting or unlocking yet.
3. **Destroyed parts** break off as wrecked debris (a wrecked part to haul to a Recycler); engines, tanks and seats are disabled
   instead. Armour is a flat reduction per hit (small arms can't hurt heavy plate).
4. **Drivers don't shoot**; milestone 2's passengers don't either (turrets come with construct weapons).
5. **Unpowered vehicles hold still** below 1.5 m/s (a parking brake), so parked and dead cars don't creep.
6. Players are pushed by constructs but never push them (except passive doors); getting run over hurts since milestone 2.
7. Parts snap to sockets in 90 degree steps on a 1 cm grid. (Free placement of a new construct on any surface came
   later: [New constructs](building-and-tools.md#new-constructs-free-placement).)
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
| Linear Actuator, Shaft Linear Drive, Free Slider | See [Linear joints](joints-and-logic.md#linear-joints) | |
| Steering Pivot | -1..1 of its limit | Follows the driver's steering |

Wheels steer only on steering pivots now (milestone 1 steered every wheel ahead of the centre of mass): a suspension
bolted to a pivot's leaf, and the wheel on it, turn with it, and its axle sets the tyre's direction. Caps: 16 joints a
construct (a flexible joint takes 2 or 3), 128 in all.

### Logic

A logic part puts out a signal: on/off (1/0) or a number. **Every wire carries the value its source had last tick**:
each tick works out every part's next value from last tick's values, then they all change at once. The order parts are
worked in never matters (bodies in list order, parts in index order, on the server), a chain of N parts takes N ticks
(a 60 Hz tick is 17 ms), and loops are fine: a NOT wired into itself is a clock, on one tick and off the next.

| Kind | Puts out | Setting (R with the linking tool) |
| --- | --- | --- |
| Seat (`seat` role) | 1 while someone sits in it; a link from it into a module carries its sitter's controls | - |
| `button` | 1 for half a second after Interact | - |
| `switch` | Interact: 1 or 0 | - |
| `lever` | Interact moves it: middle 0, forward 1, middle 0, back -1 | - |
| `xor`, `nand` | on with an odd number of inputs on; off only with every input on | - |
| The rest of the logic parts | [Logic blocks](#logic-blocks) | - |
| `analoglever` (Analog Lever) | Its value, 0 to 1, by the configure tool's output range ([below](#the-analog-lever)) | - |
| `plate` (Pressure Plate) | 1 while a player stands on it | - |
| `drivemodule`, `turretmodule` | See [Control modules](joints-and-logic.md#control-modules) | - |
| `reader` (Input Reader) | 1 while the sitter of the seat wired into it (unwired: the seat that drives) holds its key | key: fire, aim, reload, jump, sprint, forward, back, left, right |
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
or refuses with the reason: "take out the linking tool (5) to wire" (bare hands, a gun, the attach tool),
"get out of the seat first", "too far away", "a Foundation has no output", "a Switch takes no input", "an input reader
reads a seat: wire it from one", "those are already wired", "a Delay takes at most 1 input", "a wire reaches at most
8 m", "at most 64 wires a construct". Aim on a part cuts every wire on it; R on a logic part cycles its setting. Wires
are drawn only while the linking tool is out (through the parts, green while their signal is on, a yellow block at the
input end). Wiring is instant (fitting a part is what takes time).

### Passengers, riding and running people over

- The seat linked through a Driving Module to the drivetrain drives; any other seat carries a **passenger**, pinned to it like the driver. A seat
  holds one player (`SitterPlus1` on the part, so it survives parts being renumbered); a seat that breaks or falls away
  stands its sitter up. Seated players' fire, aim and reload keys go to the server (`CAct.SeatKeys`) for input readers.
- A player standing on a part **rides in its segment's frame** (`ConstructRide.cs`, board item rf01; protocol 102): the
  feet are kept in that frame (`MoveState.RideLocal`) and put back in the world by its pose each tick, exactly (no drift
  on turns, no slide on a pitching deck), and their velocity is their own, relative to the surface, so walking towards
  a truck's front goes towards its front however it turns. The local view turns with the frame
  (`ConstructRide.TurnView`). A jump stays in the frame inside the construct's bounds (or 0.3 s after leaving its
  surface) and lands where it took off. Leaving (walking off, jumping clear, landing on the map or an anchored floor, a
  mantle, a blast) adds the surface's velocity, linear and turning, so stepping off keeps its momentum, with weak air
  steering for 0.75 s (`MoveState.RideFree`). A crash (the deck's acceleration over 2.5 g across, or dropping away at
  over 1.6 g; server only) throws them off at the deck's speed before it. Everything runs in `BlockPlayer`, the step the
  server, the client's prediction and its replays share; the snapshots carry the frame-local feet (the own block
  exactly), a client replays in the frame and draws its riders in the construct as it is drawn, so lag shows as the
  vehicle a little behind, never as the rider sliding. CPose carries each body's turn rate. They count towards a
  joint's load. Anchored builds' own floors are not ridden (they never move).
- **Grip** (rf01 phase 2): a part with the `grip` setting on (a configure toggle on the Deck Plate and the Panel; the
  **Grip Deck** has it on, with a tread) holds riders on its face up to nearly vertical (about 84 degrees) and through
  up to 5 g; on it gravity doesn't pull them down the face and the walk goes along it (forward into a steep face climbs
  it). Stepping off its edge lets them go. Simplified for now: the body and the camera stay upright (world down). (This is bots' ride
  now; people: [Contraption space](#contraption-space).)
- **Boarding assists** (board item ba01, all in the shared step): coming down within 0.3 m of a moving construct's top
  lands on it at its speed (no slide off its back); walking into a construct's part steps up onto it from 0.6 m;
  jumping into a ledge up to 1.4 m high vaults onto it (a short climb in its frame, `MoveState.RideClimb`); Use on a
  vehicle's side (or on a **Grab Handle**, **Boarding Ladder** or **Step Plate**: `"boarding": true` parts, taken
  first) latches on and pulls the player up onto the nearest top within 1.5 m across and 2.6 m up (`ConstructRide.GrabOn`,
  server); a moving construct's seat is reached up to 1.6 m further (0.2 s of its speed, to 8 m/s). Friendly bots whose
  leader sits in or rides a construct take its free passenger seat (or walk to it). The "Ride frame" and "Boarding
  assist" self-tests cover each case.
- **Run-over damage** (`ConstructWorld.BlockPlayer`, `Game.SandboxRunOver`): only the **construct's own motion** counts:
  the velocity of its part at the contact (the body's motion, linear and turning, plus every joint's up its chain: a
  swinging arm, a turntable) along the contact normal towards the player, less the player's own speed away from it
  (ba01: jogging alongside a vehicle is safe), in the frame the player rides (a truck bed's walls don't hit its
  riders). Running into it never adds, so
  sprinting into a parked car only stops you, and landing on a parked car is a landing (normal fall damage only). At up
  to `Game.RunOverSpeed` (4 m/s, tunable) the part only pushes the player along at its speed; faster, it does
  `(speed - 4) x 9 x sqrt(mass / 500)` damage (the mass factor 0.4 to 2.5), at most once in 0.6 s, and knocks them out
  along the contact and up, harder the faster and heavier it is (`Game.RunOverKnock`). The same rule holds for moving
  joints and wrecks. The driver gets the kill (shown as a melee kill), or nobody for a driverless construct. The
  "Construct run-over" self-test covers each case.

### Logic blocks

The parts below are written by the part kit's `--gen-parts control_parts` (Constructs/Parts/Controls), in Seats and Logic: Controls, Sensors, Displays, Gauges, Logic gates, Memory, Wireless and bus. Their code is `LogicBlocks.cs`, `LogicInputs.cs`, `LogicDisplays.cs`, `LogicText.cs` and `ConstructRadio.cs`. Each takes one branch of the logic step and allocates nothing. What a block must keep is a setting (a Counter's or Memory Cell's `value`, so a blueprint, a world save and a joiner carry it) or its `State`, which a world save keeps. Named ports show in the linking tool's picker.

| Part | Kind | Ports in; out | Puts out | Settings |
| --- | --- | --- | --- | --- |
| XOR Gate, NAND Gate | `xor`, `nand` | up to 4 | as the gates | - |
| Math Block | `mathblock` | a, b | a op b; dividing by 0 gives 0 | `operator`: add, subtract, multiply, divide, min, max; `offset`: b while b is unwired |
| Latch | `latch` | set, reset | on from set to reset; reset wins | - |
| Counter | `counter` | up, down, reset; count, at limit | counts rising inputs | `value`, `limit` (0: none), `wrap` |
| Memory Cell | `memory` | value, store | what value was when store rose | `value` |
| Filter | `filter` | 1 | its input clamped, smoothed, then rate limited: good on a hydraulic valve | `low`, `high`, `seconds`, `rate` |
| Multiplexer | `mux` | select, 1 to 6 | the input select names, rounded (unwired: 1) | - |
| Random | `randompulse` | 1 | a new number each rising input | `value`, `low`, `high` |
| Altitude, Heading Sensor | `altitudesensor`, `headingsensor` | none | metres; degrees clockwise from north, of the part's front (-z) | - |
| Tilt Sensor | `tiltsensor` | none; pitch, roll | degrees | - |
| Level Sensor | `levelsensor` | none; level, watch | how full, %: a fluid tank's or volume's fill, the construct's fuel from a fuel tank, a store's bulk or slots | - |
| Damage Sensor | `damagesensor` | none; hit, watch | on for its `hold` after the watched part lost health | `hold` |
| Occupancy Sensor | `occupancysensor` | seat | on while that seat, or unwired the drive seat, is taken | - |
| Wireless Sender, Receiver | `radiotx`, `radiorx` | 1; none | the nearest sender on its channel that reaches it, 0 with none | `channel`; a sender's `radius` |
| Data Bus, Bus Breakout | `busin`, `busout` | 1 to 8; 1 to 8 | below | - |

- **Watch links.** A Level or Damage Sensor's second output, `watch`, carries no signal: a link from it ends at any part at all, on the port "Watch this part" (`ConstructRoutes.WatchPort`), and that is the part the sensor reads. It saves in a blueprint as the port `watch`. Same construct only.
- **Data Bus.** Eight signals into a Data Bus, one data link to a Bus Breakout, eight signals out, a tick later per end. The link is an ordinary data link, so it lies in a Loom or a tray like any other. A breakout reads a Data Bus on its own construct; across constructs its outputs are 0.
- **Wireless.** A sender is heard between constructs. Senders are gathered once a tick into a fixed list of 256, so a signal takes one tick more than a wire. Channels aren't private: any receiver on the channel in range hears it.
- **Exact numbers.** A pose's signals are tenths up to 12.7. These parts also have `CPart.Reading`, which reaches clients exactly: all of them in a CBody, and what moved five times a second in `CBodyKind.Reading` (protocol 153).

### Keys and panels

- **Numpad** and **Code Lock** have twelve real keys (`"use": true` boxes with `"key": "7"`). Aim at a key and press Use: `CActKind.KeyPress` names that key, so only it glows. Use on the screen or the body opens a small typing box: the view stays, digits go in, Enter accepts, Esc leaves. A Numpad's Accept puts the number out (`decimals` places the point) and pulses `accepted`; Clear wipes the entry and the output. A Code Lock's Accept with its `code` turns it on, for `hold` seconds (0: until Clear); a wrong code turns it off. `invert` is on until opened, for wiring straight into a hinge's Lock. Clients only ever hear how many digits were typed. Anyone in reach may press keys. The code is a setting, so it has the configure tool's owner rule, and it does travel in a CBody.
- **Rotary Knob** and **Slider** are Analog Levers of another shape (`"rig": "knob"` or `"slide"` with `"travel"`): the same drag, wheel and settings; a knob's `snap` starts at 0.1, its stops. A knob's mouse up raises it.
- **Toggle Switch** and **Rocker Switch** are switches, **Mushroom Button** a button; `"rig": "flip"` and `"press"` move their boxes by whether the part is on. Button and Switch both have `latched` and `hold`.
- **Key Switch** turns for its owner, for anyone on a construct nobody owns, or with a **Key** in your hands; any Key turns any Key Switch.

### Displays

Each has a panel ([Display panels](joints-and-logic.md#display-panels)) whose `source` is a `logic.*` entry of `PanelSources`. Small, Medium and Large. Everything shows on any peer; a part with no power says so.

| Part | Source | Shows | Settings |
| --- | --- | --- | --- |
| Number Readout | `logic.readout` | the sum of its inputs, as big as it fits | `decimals`, `unit` |
| Bar Gauge, Tall Bar Gauge | `logic.bar`, `logic.barv` | a bar over the whole screen, `low` to `high`, no number | `low`, `high`, `ramp` |
| Dial Gauge | `logic.dial` | a needle over 270 degrees | `decimals`, `unit`, `low`, `high` |
| Graph Screen | `logic.graph` | the last 30 s over the whole screen, a sample each 0.5 s, no number; `low` = `high`: it fits the line | `low`, `high`, `ramp` |
| Light Bank 4, 8 | `logic.lamps` | a lamp an input, labelled | its text: labels, comma-separated |
| Text Sign | `logic.sign` | its first message; with a wire on, its second | its two messages |
| Compass | `logic.compass` | a tape, the way you face looking at it | none, no wiring |
| Altimeter | `logic.altimeter` | how high | `decimals`, `unit`, no wiring |
| Speedometer | `logic.speedometer` | a needle, and the speed | `units`, `low`, `high`, no wiring |

- **Colour.** Every display has `ink` (a colour; black: its own colours): text, needle, trace, bars and lit lamps take it; the frame keeps the state's colour. Bars and graphs have `ramp`, how the colour follows the value: display colour, green yellow red, gradient, heat (`PanelRamp`). Both are plain settings: saved by key, in blueprints, sent in CBody.
- **In the inventory** a display's tile looks at its screen and shows the real panel: `LogicDisplays.Preview` fills the part's own card from a scratch part with a typical reading, and `PanelRender.DrawOnce` draws it into the tile, so a preview can't drift; made once with the atlas, nothing a frame.
- **`PanelCard.Draw`** picks a shape; `Value`, `Trace`, `Lamps` and `LampBits` set it, and `Fit` makes one line as big as the panel takes. A needle or a graph's line is a row of small upright quads, so a panel still has one shader and one vertex format.
- **A graph** is sampled on each peer that draws, whether or not it is in view, into `CPart.Trace`. A joiner's starts empty.
- **Text** is on the part (`CPart.Text`, `Text2`; up to 60 ASCII characters). Use on a sign or Light Bank opens the typing box on what it shows; the configure tool's panel has a box for each. Both are `CActKind.PartText`, under the configure tool's owner rule with no tool needed. A blueprint step has `"text": ["one", "two"]`. A world file holds it as settings under `text0.0`, `text0.1`... three characters a float, which an older reader skips.

### The Analog Lever

A number from 0 to 1 set by hand, for throttles and speed settings (`Source/Constructs/AnalogLever.cs`, the hand in
`Source/App/Program.SandboxLever.cs`; the part kit's `--gen-parts control_parts` writes Analog Lever, Medium and Large, in
Seats and Logic > Controls). Wire it into any data input: an engine's throttle port, a piston (0..1 of its travel).

- **The hand.** Hold Use (F) on it and move the mouse: the view stands still, the handle goes the way the mouse goes on
  the screen (seen end on, mouse up raises it), and letting go leaves it there. That works with every tool, and with a
  gun or bare hands: Use fits and removes nothing. The exceptions are the attach tool's join (a part touching another
  construct), and the physics gun while it holds something. LMB does the same with the **Hands** out: every other
  tool's LMB is its own, and a gun fires. A press with no move changes nothing.
- **In a seat** the same works on a lever in reach of the seat (3.2 m). A press with no move is Use as ever: out of the seat.
- **The wheel** nudges the lever under the crosshair by 0.05, or its snap step: with the Hands, the attach tool or no tool,
  and nothing held; any tool in a seat. While dragging, any tool.
- **Settings** (the configure tool): `value`; `snap` (0: smooth); `low` and `high`, what it puts out at 0 and at 1, so
  1 to 0 or -1 to 1 work; `spring` and `rest` (off by default): let go, it glides back at 1 a second. Dragged or set,
  the value is a setting, so blueprints and world saves keep it.
- **On the wire** (protocol 133). The client sends `CActKind.AnalogLever` at up to 10 Hz, every 0.2 s while it holds still
  (a spring waits 0.5 s past the last one) and a last word on letting go. The server checks the reach (use reach plus
  1.5 m, as you may be moving) and the snap step, sets the setting, and sends `CBodyKind.Analog`. Anyone may use a lever,
  as any lever; the configure tool's slider has the owner's rule. The hand's own PC shows its own value at once and for
  0.6 s after letting go, so nothing snaps back; everyone else eases towards what they hear. A joiner gets it in `CBody`.
- **The handle** is drawn from the value, not from the part's signal (a pose's signals are in tenths): a `"lever"` rig
  box tilts about the part's x through its `"pivot"`, 35 degrees either side of upright, upright at 0.5. It adds no
  collision, so a hidden box stands where it swings for the crosshair.
- The lever held is a part index another player's edit can shift, so it is stamped when taken and checked each frame:
  a hand on a lever that changed just comes off.
- **The Joystick** (Joystick, Medium and Large, from the same script) is an Analog Lever with `"axes": 2` in its logic
  block (`ConstructPartDef.Stick`): a stick in a square gate that goes any way. `value` is its X (the tip towards the
  part's +x) and `valueY` its Y (towards +z, as a lever's), each with a rest (`rest`, `restY`); `snap`, `low`, `high`
  and `spring` serve both. It puts X out on its first output (`x`, the part itself) and Y on its second (`y`,
  `CPart.Out1`), so link Y from the `y` port in the linking tool's picker. Out of the box it springs back to the middle
  and puts out -1 to 1. The hand drags it the same way: the mouse along the screen's picture of the part's +x moves X,
  square to that moves Y; the wheel leaves it alone. Both axes ride `CActKind.AnalogLever` and `CBodyKind.Analog`
  (a u16 Y each since protocol 211, 0 on a lever) and are settings, so saves, blueprints and joiners keep them. Its
  boxes are a `"stick"` rig: tilted about the part's z by X and its x by Y, 35 degrees either way (`AnalogLever.StickTurn`).

### The Nudge tool

Moves and turns **whole constructs** exactly, by a triad of arrows and a set of arcs (`Source/Constructs/ConstructNudge.cs`, the client in `Source/App/Program.SandboxNudge.cs`, `Source/Game/GameSandboxNudge.cs`, the ring `SandboxNudgeModes`, drawn by `NudgeRender`; protocol 156). It is straight up on the tool wheel.

- **Its target** is everything the Selector has selected (every body with a part selected: the parts stay fixed in their bodies; a single part is never moved on its own), else the construct you click. RMB lets a clicked one go.
- **Move.** The triad sits on the pivot, drawn over everything: X red, Y green, Z blue. Aim at an arrow, hold LMB and drag: the view holds still and the mouse's travel along the arrow on screen is the move. An arrow end-on to the view can't be taken.
- **World or local (R).** World: **Z runs down along gravity** (a planet's, a generator's: `Nudge.Axes`), X as near the map's X as that lets it be, Y square to both. Local: the body's own axes under the same names, so an unturned body's are the world's.
- **Rotate.** Rings take the arrows' place, one about each axis; aim at one and drag round it. **Rotate snap** turns in steps of the snap angle, **Rotate free** at any angle.
- **The ring** is Rotate snap, Move (the default, in the middle), Rotate free. **The mouse wheel** sets the move step (free, 1, 5, 10, 25, 50 cm, 1, 2, 4 m; 10 cm to start) or the snap angle (1, 5, 10, 15, 22.5, 30, 45, 90 degrees; 15 to start). They last the session. RMB or Esc during a drag puts it back.
- **The pivot** is one body's centre of mass, or the mass-weighted centre of a selection's bodies. A local selection uses the first body's axes.
- **On the server** the client asks for the whole move and turn since the drag began (`CActKind.NudgeSteer`, about 30 times a second, unreliable; `NudgeEnd` for certain when let go). A nudged body is **posed, not pulled**: `Construct.NudgeBy` holds it out of the physics, and `ConstructEdits.NudgeTick` eases it on over 0.05 s (at most 30 m/s, 6 rad/s), landing exactly on what was asked. So a step is a step, and a selection moves as one. That is where it parts from the physics gun; it shares the gun's refusals, its range (30 m, 12 m in survival), the rule for what blocks, and the holder's drawn lead (`ConstructGrab.LeadMin`, `LeadSeconds`). A body in either is refused by the other.
- **What stops it.** Only a **new overlap** with another construct, a player or the map, tested tick by tick; a piece already in something may move, and what a body carries, is coupled to or moves with never blocks. It stops there with "something is in the way". The map is tested at 128 boxes a tick, in turn, so a very big body may go a few ticks into a wall first.
- **Afterwards** a frozen or anchored body stays so; a loose one that was moved or turned freezes where it landed, to build on (`NudgeLand`; not with a driver in its seat, and not a cancelled drag). A drag that moved nothing leaves it to the physics. A coupled partner isn't moved: the coupling pulls it once the physics runs.
- **Who may.** As for any edit: your own, wrecks, loose parts and the map's; never one someone holds or someone unfriendly is aboard; the contested rule. Survival adds the physics gun's 500 kg cap and leaves anchored bodies alone (frozen ones may move). In creative anything goes.

**Self-test** "Nudge tool" (`SelfTestNudge.cs`), and "Interaction sweep" takes it to every part.

### Contraption space

A player on a construct lives in its frame (`Source/Constructs/ConstructRideMove.cs`, the look and drawing in
`Source/Game/GameSandboxRide.cs`; protocol 130). This replaces the plain ride above for people; bots keep the plain ride.

- **The frame.** A branch of `World.Simulate`, like noclip: with `MoveState.RideUp` set, the tick runs `ConstructRide.Step`:
  the capsule mover's walk, step-up, ground snap, jump and stances (`SweptMover.FrameStep`, the same code the world mover uses)
  in the coordinates of the segment under the feet, against shapes gathered there. `RideLocal` (feet) and `RideVel` (their
  velocity) are the whole of their movement; `Position` and `Velocity` are copies for whatever reads the world. The construct's
  own boxes come from its joints alone (`SegPose`), never its world pose, so nothing about its speed, turn, tilt or roll
  reaches the mover: the same inputs give the same local state at 0 and 200 m/s, tilted or inverted, on the server, in the
  client's prediction and in its replays. A segment is the moving joint's (`MoveSeg`): a freeform attachment is the same frame.
  With `RideUp` unset, nothing here runs: every other mode and every non-rider moves as before.
- **Gravity and up.** One direction for the whole construct, taken when boarding (`ConstructRide.UpFor`, in the rest frame): the
  drive seat's up; with no seat, the direction most of its faces lie across, the way nearest the world's up. If the surface
  under the feet doesn't lie across that (a car on its side), that surface's normal is the rider's up. A joint's segment carries it
  round with it: a raised drawbridge is level to who stands on it, and crossing back rolls the view. Gravity is the world's
  strength along -up in the frame, also in space. There is no Up part or setting yet.
- **Grip faces** stay, and do more: a grip face's own normal becomes the rider's up, at any angle, so they walk up a grip wall
  and across a grip ceiling with the body and view rolled onto it. Ordinary faces no longer need Grip to be held on: a construct
  tilted 80 degrees is level to its rider. Stepping off a grip face onto an ordinary floor goes back to the construct's up.
- **Boarding.** Standing on a top as before, or walking or jumping into a face that is the construct's own up but too steep to stand
  on in the world (a panel raised at 60 degrees; `SteepBoard`; a truck's side isn't its up, an overhang never takes anyone).
  Stepping onto one from the ground works to about 65 degrees; steeper, jump at it. A frozen build's floor is only ridden if its
  up leans over 15 degrees from the world's; a level base stays the world's ground.
- **Leaving.** Walking off an edge or jumping clear: off every surface they stay in the frame (falling along its up) inside the
  construct's bounds (0.5 m round them, 2.5 m along up) or for 0.3 s; past that they leave with their velocity plus the surface's
  (world gravity from there, weak air steering for 0.75 s). So jumping on a raft at 200 m/s lands where it took off. Also: a
  blast's push; touching the world's geometry (below); a seat; noclip (which ignores the frame); a crash.
- **A crash** is a jolt over 15 g (30 on a grip face) in the deck's acceleration less what its own turn explains (`CrashAccel`), so
  a hard turn at speed never throws anyone, and a wall does. Thrown riders leave at the speed before the jolt, not rejoining for 0.5 s.
- **Other things in the world.** Other constructs are in the frame's shapes, each posed relative to the deck: they block, carry
  (standing on one joins its frame) and, closing at over 4 m/s, hit as a run-over does; over 8 m/s they scrape the rider off. The
  map, terrain and movers are not in the frame: each tick the body is tested against them as it stands in the world (the upper
  body when standing, the whole capsule in the air); touching them ends the ride at the deck's speed. So a low bridge at speed
  takes you off the deck, and walking off a ramp onto the ground just lands. A ride ended that way can't start again for 0.5 s.
- **Doors, plates, lifts.** Walking into a door leaf swings it; pressure plates, a joint's load and spring feet count riders;
  a lift's deck is a segment, so it carries its riders exactly, and stepping off changes frame.
- **The look.** The client keeps the look in the frame (`RideLookSync`): any change to `LookYaw`/`LookPitch` (mouse, recoil) is a
  change in the frame, and they are worked out again from it as the construct is drawn, so every aim ray and tool that reads
  them is right whichever way it's rolled. Input carries the look in the frame (a trailer on `Msg.Input`); the server works out
  the world look from it and its own pose (`RideLookIn`), so reach and aim agree in the frame. The camera's up is the frame's,
  exactly; moving onto, off or between frames rolls over about a quarter of a second (`RideCamUp`). The head bob and foot IK are
  off while riding.
- **Drawing.** Every frame, the host and each client draw a rider from their frame-local feet and the construct's drawn pose
  (`SandboxFrameRender`): nothing lags between them. Remote riders use the snapshots' feet in the frame and their up.
- **Tools.** Actions name parts and sockets, and points in a body's frame, so they mean the same at any speed; the server judges
  reach from the rider's eye, which is exact in the frame. Riders on the server are put back in the frame after the
  constructs' step (`SandboxRidersFollow`) whether or not an input came. The host's input and overlays see the ridden construct at its
  drawn pose (`RideDrawnPose`; a host draws between ticks, and 3 m a tick matters to an aim ray). The physics gun holds in the
  frame: its target and turn are relative to the deck (`LookQ`, `GrabFrameVel`, `GrabFrameSpin`), and `HeldSweep` tests the
  piece's move in the deck's frame. Pieces cut free and wrecks already take the deck's velocity at the cut, a dropped part now too.
- **Shots.** At deck speed over 2 m/s a rider's shot uses the server's eye and look (the client's world ray is a round trip
  behind). A target on the shooter's own deck is rewound in its frame. Hit boxes stand along the rider's up.
- **Space.** A rider stays in the frame through a warp, a jump and being parked: they follow the pose. A ship on rails throws nobody.
- **Saved worlds.** A rider's place is kept in the frame (the `Riders` section, which older readers skip), and they come back
  aboard where they stood, relative to the construct.
- **Cost.** Eight riders on a 7,500-part, 300 m deck: 0.16 ms a tick in the container, nothing allocated.

### Seats: riders as part of the construct

- **Seat types** (`SeatKind`, from where the seat is and what it is linked to): the drive seat is the **driver's**; a seat with a Turret Module, or a seat on a moving joint's
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
- **Getting in and out** keeps its rules (Use on a free seat; F gets out, beside the seat if clear), with one change on the move, below.
- **Coming down onto a construct** (`ConstructWorld.BlockPlayer`). A body coming down lands on any part's top that faces up in the
  world, whichever way the part is turned (an inverted or tipped hull too), with its centre up to `ConstructRide.EdgeHold`
  (0.24 m) past the edge, as the frame's capsule stands on an edge, and a tick's fall relative to the part always reaches the
  top, so a fast fall doesn't pass it. Without these the sideways push-out took them off the rim or beam. The landing's tick is
  drawn from where the feet were (`RideEntryDraw`), and a client's correction on a deck is eased up to 8 m (a fast deck on a
  client's copy is a round trip behind). Frozen upside down a rider stays on (level is judged by their own up). The world
  still counts: a hull that carries its rider into the ground lets them go, and a construct can't run over someone who rode it
  in the last 1.5 s (`Game.RunOverGrace`); it only pushes them. Under a part (a hull's roof) a body stops, it isn't pushed out
  sideways. `SteepBoard` only takes feet at the face, so a tall thing's long side (its up by area) doesn't take a body coming down
  on its end. Sandbox has no 60 m sky limit (`World.OpenSky`): a body put back under it landed inside anything built taller.
- **Getting out on the move** (`ConstructRideExit.cs`, `Game.SandboxStand`). On a construct moving over 1 m/s, or with no ground
  under the plain exit (a hovering hull, a ship on water), the search looks for a spot on the construct first: 8 ways out of the
  seat in its own directions and the rider's up, 0.75 to 3.2 m out, nearest first, in sight of the seat before any other. A spot
  needs a floor under the feet that moves with the seat (this construct or one it stands on, a car on a ferry; not a wheel),
  room for the body in the turned frames (`ConstructWorld.CapsuleClear`) and no map geometry. The rider goes through
  `ConstructRide.End`, as if they had landed there, so they are in the frame from their first tick. With no spot they take the
  plain exit at the seat's velocity (`RideFree` as for stepping off), to the side of the construct's way, never in line with
  it. The local player's seat now comes with their own snapshot entry, not the ~10 Hz hands state, so the seat and the move
  always change together. Parked on the ground the exit is the plain one. "Ride frame" (`SelfTestSeatExit.cs`) tests this
  with a host, a client and a watching third peer at 150 ms lag, 10 ms jitter and 2% loss, plus a real buggy.
- **Dying seated**: the ragdoll starts in a seated pose at the seat, with the construct's velocity there, and goes limp
  (no death reaction).
- The **"Seated riders"** self-test: in a game on the yard a driver and a passenger in the wired buggy and a gunner on
  the Gunner Turret stay within 4 mm of their seats' local offsets (feet and eyes, every frame between ticks) while the
  buggy drives at up to 12 m/s and turns 140 degrees and the turret turns; no walk cycle or footsteps; the look limits; a
  seated ragdoll; no allocation; over a loopback two clients agree on the seat, the offset and the seat-relative look.

### Decisions made for milestone 2 (the developer may change them)

1. **Joints stay reduced coordinates**: a joint is a number on one body, not a constraint between two free bodies, so it
   can't come apart or explode; its load is gravity on what it carries, and the body's mass properties use the rest pose.
   Since the joint-collision fix contacts on the parts beyond a joint drive that number directly ([Joint collision](network-and-scale.md#joint-collision)).
2. **Every wire takes one tick** (the default the brief suggested): order never matters, loops are clocks. Chains of
   many gates are a little slower (17 ms a part).
3. **Wires join parts of one construct only** (a button on a wall can't run a separate gate yet), up to 8 m long.
4. **Steering is a joint**: wheels steer only on steering pivots, which follow the driver unless something is wired in.
5. **The drivetrain is strict**: an engine drives nothing without a transmission touching it and a differential it feeds
   (the scripted build test fits both); only wheels on a differential are driven, the rest roll.
6. **Seats**: a seat controls what its modules give it ([Control modules](joints-and-logic.md#control-modules)); any other seat is a passenger seat.
7. **Logic settings** are changed with the linking tool's R (no separate configure tool); controls (buttons, switches,
   levers) work with any hands, as Use. (Since the configure tool: R steps through the old values as presets; every
   setting is in the configure tool's panel.)
8. A **pressure plate** is pressed by players only (not by constructs or loose parts).
9. **The attach tool and linking tool** sit on 4 and 5, the kill-streak keys (Sandbox has no streaks; the Weapon slot 4
   binding still works too), so no new key binding was added.
10. **Run-over kills** show as melee kills and credit the driver.

## Next: milestone 3 and later

- **Salvage** (done, above). Still to come with it: materials, repair, saving inventories.
- **Physical storage** (done, above). Still to come: carrying limits (the developer's question), more storage parts, saving with the world.
- **Construct weapons** (done, above). Still to come with them: artillery (craters are in: docs/sandbox/TERRAIN-DIGGING.md), guided or
  flying rockets, a zoomed gunner view through an optic, enemies manning guns.
- **Resources:** [gathering](gathering-and-salvage.md#gathering-decision-14) and [refining](production.md#refining-decision-14) (done, above); assembling comes next. **Enemies** that drive and defend constructs, and **factions**.
- Links between constructs and power (done, above).
- Saving constructs with the world, then the large world, streaming and points of interest.
- A bigger map built over several iterations, as the developer asked (bumpers, fenders, gauges, batteries and gears came in milestone 3).

## Milestone 1's list, as milestone 2 left it

- Done: the linking tool and logic parts (seat -> input reader -> piston), motorised hinges, turntables, pistons, steering
  as a joint, transmissions and differentials as parts you can shoot, passengers, standing on a moving deck, run-over
  damage, lights (the Lamp).
- Still to come (above): construct weapons, saving, the large world, more parts, a bigger map (salvage and debris
  expiry came in milestone 3).
