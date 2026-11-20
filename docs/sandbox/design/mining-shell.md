# The Mining Shell, and shells made of modules

The user (2026-11-17): "Add a new form of robot shell. A new shell that's 50% larger, that has a large loose ore hopper on its
back, and one of its arms is always going to be a drill. This is going to be a mining shell. So if the player or the faction
needs to handle mining better, there's better tools for them. Also, there's going to be shell customization in the future.
It's not in yet, but prepare to make things modular."

## How shells work now (before this change)

- **The part.** `Constructs/Parts/Shells/RobotShell.json`, written by the part kit's `shell_parts` (`Source/PartKit/Generators/ShellParts.cs`):
  a one-part construct with `"shell": {"body": true}` and a channel. `ConstructShell.cs` parses the key and finds the shell a
  chair wakes (`Find`, `Wakeable`, docked shells on a Robot Shell Dock).
- **The swap** (`Game/GameRobotShell.cs`). Sitting 2 s in a Shell Control Chair wakes the player in the nearest free shell on its
  channel: the shell's construct is removed and the player is moved onto it, so a shell is played as a player is (the same
  mover, prediction, tools, seats). `ShellRun` keeps the body's place, health, hands and pack; `ShellReturn` stands the shell
  again as `run.ShellDef` with its channel, wear and health in its config.
- **Every peer** hears `Msg.Shell` (player, on, chair, seat, 8 wear bytes) and draws the player as a robot (`BodyModel.RobotVariant`).
- **Wear** (`ShellWear.cs`, `GameShellWear.cs`, `Constructs/ShellWear.json`): eight works, the move share (`ShellMoveMult`), the
  hit split (`ShellHit`).
- **The body's size** is fixed: `World.PlayerRadius` 0.4, `PlayerHeight` 1.8, `EyeHeight` 1.6, `HeightOf`/`EyeOf` per stance.
  Nothing in the movers, construct blocking, ladders or hit boxes knew of another size.
- **Hand mining** today is the ground drill (`GameSandboxSculpt.cs`): a scoop of dirt, stone or sand into the inventory, never
  ore. Ore comes only from a Drill Module (`ConstructGatherer.Work`, `ResourceField.Tap`, a spot worked out after 1,200 units).

## The Mining Shell

A second shell part, **Mining Shell** (`Constructs/Parts/Shells/MiningShell.json`, same generator), woken from the same chairs
on the same channels. Decided (defaults; the user may change them):

- **1.5x the size.** One number, the frame's `scale` in `Constructs/Shells.json`. While a Mining Shell is worn the body is 1.5x
  in every direction: capsule radius 0.6 m, standing height 2.7 m, eye 2.4 m, crouched 1.8 m, prone 0.9 m. It is the same mover
  (`World.BodyScale`, set from `MoveState.Frame` inside `World.Simulate` and around the construct blocking), so the client
  predicts it exactly as the server runs it. The step height (0.55 m) and the mantle reach stay a person's, so stairs work as
  for anyone and the robot can't climb a wall a person can't.
- **What it fits.** Standing it doesn't pass any shipped door (the tallest is 2.25 m); crouched (1.8 m, 1.2 m wide) it passes
  a 1.25 x 2 m bulkhead or slide door and the 1.5 x 2.25 m one. Ship decks of 2.4 m are crouch-only. Ladders: its capsule
  climbs a ladder whose shaft is 1.2 m clear; most ship ladders aren't (it says "no room"). Seats: it sits in a seat as a
  person (the seated pose isn't scaled; a robot of this size is meant to work outside). This is the point of the frame: a
  ground and hold machine, not a crew body.
- **Speed, mass, health.** Speed x0.85 of a person's (`speed`), hits taken x0.7 (`damage`: thicker plating); health is a
  shell's (`World.MaxHealth`), wear as any shell. The part is 140 kg (the Robot Shell 38).
- **The ore hopper** (back slot). The Mining Shell's part is a bin: `storage.bulk` with 5,400 units (three minutes of drilling) of one material at a time,
  ores, stone and sand only (`takes`). Standing, it is a bin like any other: F takes a load out, its look line shows the fill,
  and its load adds mass. Worn, the hopper's contents go with the player (`ShellRun`, `SandboxHands.Hoppers`) and come back
  into the part when it stands again, so nothing is lost by leaving.
- **The fixed drill arm** (right-arm slot). The right arm is a drill, always, and the shell's only tool (the user, 2026-11-17):
  the module's `"tool": "GroundDrill"` forces it. While the shell is worn the tool wheel, the number keys and the gun are
  refused on both sides (`ShellToolRefusal`: the client says why and sends nothing, the server refuses the act and puts the
  drill back out every few ticks, `ShellForceTool`); leaving the shell gives the body's own tools back. A scoop does what the ground drill does
  (dirt, stone and sand into the inventory, the hole in the ground) and, over an underground deposit the drill arm's tier
  works, also taps the deposit through `ResourceField.Tap` into the hopper at the module's `rate` (30 units a second), counted by the time between scoops (a first scoop after a pause counts at most 0.5 s), so a slower
  or faster drill motor doesn't change it. The spot rules are the Drill Module's: a 3 m spot gives 1,200 then says "this spot
  is worked out". Tier 2: iron and copper, not gold (tier 3) or levite (tier 4, deep). A full hopper or another material says
  so and the scoop still digs. Boring into ore, the arm's hole ends `DrillShaft.Depth` (1.5 m) under where that bore first
  struck the ground, as a Drill Module's does: over a deposit the arm can work, a scoop on a floor deeper than that within a
  spot's width only taps (`ShellDrillBored`); anywhere else (hillsides, cliffs, caves, ground with no ore) it digs as the ground
  drill does, so the crosshair stays on the floor within reach and
  a held button keeps the whole rate; a player in the hole can still mantle out.
- **Unloading.** F at a Materials Bin, a Storage Volume, a Tipping Bunker, an Ore Hauler's bed or a refinery's input tips the
  hopper first (as much as fits, the same `Deposit` and `Feed` a carried load uses); with an empty hopper F does what it did.
- **Getting one.** Survival: an Assembler recipe (tier 2: 70 steel, 14 copper, 8 rubber, 3 Circuit Boards, 60 s), then wake it
  from any Shell Control Chair. Creative: the parts menu, free, as every part.
- **Seen.** Every peer draws a Mining Shell wearer 1.5x, with the hopper on the back (its fill shown in the ore's colour) and
  the drill for a right forearm; `Msg.Shell` carries the frame and the hopper's material and fill (protocol bump).
- **Factions and crew.** There is no crew Miner job yet (`CrewJob` has none), so nothing in the crew code uses the shell now.
  The frame lists `"jobs": ["miner"]`, so a Miner job, when one is made, finds the shells that suit it from the data.

## Modular shells

`Constructs/Shells.json` (format `call-of-slop-shells/1`; `Source/Constructs/ShellFrames.cs`):

- **slots**: the slot names in a fixed order: `head`, `back`, `leftArm`, `rightArm`. The order is a wire and save order; a new
  slot goes at the end.
- **modules**: what can go in a slot. A module is meant to be a whole functional part (the Shell Gantry, below, sets them on a
  parts table); a module may name that part with `"part"` once it exists (none does yet). Each has a `name`, the `slot` kind it fits (`head`, `back`, `arm`) and a `kind` the code
  knows (`head`, `pack`, `hopper`, `hand`, `drill`), plus that kind's numbers (a drill's `rate` and `tier`, a hopper's
  `takes`). Today's modules: Optics Head, Power Pack, Ore Hopper, Hand, Drill Arm.
- **frames**: a shell body. Its `name`, the `part` that stands as it, `scale`, `speed`, `damage`, `jobs`, the module in each
  slot (`slots`) and which slots are `fixed` (can't be swapped by customisation: the Mining Shell's drill arm and hopper).
  Frame 0 is the Robot Shell (Standard), described in the same layout with exactly its old numbers, so nothing about it
  changed. A frame's index is its byte on the wire (`MoveState.Frame`, `Msg.Shell`); a new frame goes at the end.
- **The code reads modules, not frames.** The drill rule asks "is there a `drill` module in an arm slot", the hopper "a `hopper`
  in the back slot", the renderer draws each slot's module. So customisation only adds modules (data, a look and, for a new
  kind, its rule) and a way to choose them (UI, and a per-shell record of the choice in the part's config).

### How a worn shell shows its modules (the user, 2026-11-19)

"The shell parts that are currently equipped should be visually apparent on a possessed shell", with "more visual variance"
between the limbs and "a runner leg should look more digitigrade than human". `Scene/Rigs/ShellModuleNodes.cs` draws every fitted
module on the wearer's rig from its own part's model (ShellParts.cs), read from the worn kit, so a gantry swap shows at once on
every peer: the head on the person's head node (their head hidden), the back module on the back (the ore hopper and the drill
arm, which have no part, stay ShellKitNodes'), a Hand's forearm over the arm's, a tool arm out of the shoulder with its forearm
turned with the look, and a leg's boxes on the leg's bones by its `"joints"` (heights on its part: hip, knee, optional hock,
ankle). A leg with a `"hock": [up, back]` walks digitigrade: `LegAnimator` reaches the thigh and shin for the hock, up and behind
the foot's ball, and a metatarsal runs down to the ball (the knee forward, the hock back). The looks: Standard Leg a person's
leg, Heavy Leg thick with plates and a wide foot, Runner Leg slim on clawed toes with amber springs; Optics Head two lens
barrels and a dome, Plated Head a helm with a slit visor; Power Pack a case with cell caps and vents.

**Tool arms** (`"kind": "tool"`, a `"tool"` and a `"part"`): the Welder Arm (attach tool), Cutter Arm (remover), Grip Arm
(physics gun) and Sprayer Arm (paint). A gantry fits them in either arm slot. Defaults taken: a shell with any tool module
(these, or the drill arm) holds only those modules' tools (the tool wheel and number keys refuse the rest, as for the Mining
Shell's drill; with two tool arms either is picked); the arm is its tool's look in third person (no held tool beside it) and
in first person (its forearm, aimed at the crosshair, on the right whichever side it is fitted); the immediate-mode body
(`PlayerRender.DrawImmediate`, off by default) draws the person as before.

## The user's answers (2026-11-17)

1. **Collision**: 1.5x, as built (it crouches through doors; ship decks are crouch-only).
2. **Controls**: "Drill is the shell's only tool." Primary fire drills with the arm; no other tool or the gun while worn
   (data: the right-arm module's `"tool"`, so a future module can force its own).
3. **Numbers**: 30 units a second, really sustained (the hole stops at 1.5 m so the scoops keep landing), and a hopper of
   three minutes of mining, 5,400 units. Speed x0.85, hits x0.7 and the tier 2 recipe stay.
4. **Where modules swap** (future, not built): a **Shell Gantry** station. "I would like a new station to be constructed, a
   vertical gantry system that the suit will be held in. From there, arms on the gantry will pick up and place pieces in the
   station storage hopper. When I say hopper, I mean something similar to the blueprint construction station, where it has a
   table that can hold whole functional parts." So the shell stands in a vertical gantry, the gantry's arms take modules off
   and fit them, moving them to and from a parts table like the Building Station's hold. The layout above fits it: a module
   is a whole part (`"part"`), a frame's `fixed` slots are the ones the gantry may not change. Before building the station,
   show the user a layout image and get approval (user, 2026-11-17).

## Defaults taken without the user (2026-11-18, the user away; each may be changed)

- **Reach** of the drill arm is the ground drill's times the frame's size (5 m to 7.5 m), on both sides, so the scoops reach
  the floor of the arm's 1.5 m hole and a held button keeps 30 a second (measured: 316, 619, 907 units at 10, 20, 30 s).
- A 3 m spot still gives 1,200 units (the Drill Module's rule), so one spot lasts 40 s of the arm: step a few metres on.
- **Saves**: a world saved while someone wears a shell stands it for the capture where they are, as worn and damaged, with
  the hopper's load in its bin, and takes it away again in the same tick (`ShellStandForSave`): after a load the shell stands
  there and its wearer is in the chair.
- **Other bodies**: players push apart by each one's own radius; construct blocking, legs' feet, lifter pushes, the seat's
  way out and ladders use the body's size. A lifter's wait for players standing under it (`PlayerSpots`) still uses a
  person's size.
- The shell's only tool is refused on the client with a line on screen (nothing sent) and on the server; a part picked up
  or a seat left puts the drill back out within a sixth of a second.

## Not yet

- A crew Miner job (none exists) and factions spawning Mining Shells.
- The seated pose, a first-person view of the drill arm (the third-person rig draws it), drill sounds of its own.
- Tiers of drill arm (data: a second `drill` module with another `tier`).

## Shell Gantry layout (user, 2026-11-18)

Drafted with the user over three pictures; build to this once the user approves the final picture.

- **Bay:** square, 5 m x 5 m, 5 m tall (the Mining Shell is 2.7 m). The shell walks in from the front (or its wearer wakes into it from a chair) and stands facing the front; the front stays open.
- **Parts table:** behind the bay, past the rear frame; a hold like the Building Station's, a slot per module (one tall slot for a hopper).
- **Arms:** five, all mounted on the rear frame: two on the left (upper, lower), two on the right (upper, lower) and one reaching over the head. Each reaches round from the rear, takes a module off the suit, sets it on the table and fits the chosen one.
- **What each arm works on:** upper arm per side = that side's arm module; lower arm per side = that side's leg; head arm = the head. This adds **left leg and right leg slots** to `Shells.json` (new slots go at the end of `slots`: the order is the wire's). The back pack (hopper) is fitted by an upper arm. A frame's `fixed` slots are never changed by the gantry.
- **Bay and table are separate parts** (user, 2026-11-18): the parts table is placed on its own behind the bay and linked to it like a machine's hold; the gantry arms must still reach it, so it is placed within their reach behind the rear frame.

## What was built (Shell Gantry, 2026-11-18)

- **Slots and modules** (`Constructs/Shells.json`, `ShellFrames.cs`): `leftLeg` and `rightLeg` at the end of `slots` (six now; a `leg`
  slot kind). New modules at the end of `modules`: Standard Leg, Heavy Leg, Runner Leg (`"speed": 1.05` each, so a pair is x1.10) and
  Plated Head (`"damage": 0.9`). Any module may carry `speed` and `damage` (times the frame's; 1 if not said). The Robot Shell's legs
  are Standard Legs, the Mining Shell's Heavy Legs: no number changed for either. Every module that comes off has a `"part"`; the Ore
  Hopper and the Drill Arm have none (they are the Mining Shell's `fixed` slots and are never taken off).
- **A shell's own kit.** A byte a slot (0: its frame's module, else 1 + the module's index), kept in three hidden mask settings on every
  shell part (`CfgKey.ShellKit0..2`, `ShellFrames.ReadKit/WriteKit`): it rides world saves, blueprints and CBody. Worn, it is
  `Player.ShellMods`, fitted into `Player.ShellKit` (`ShellFrames.Refit`, no allocation), which every rule reads (`ShellFrames.Worn`:
  pace, hits, the hopper, the drill, the forced tool). `Msg.Shell` ends with the six bytes. A shell saved before has no kit: every
  slot its frame's.
- **Parts** (part kit `shell_parts`): **Shell Gantry** (`Shells/Gantry`; a 5.5 m deck, a 5 x 5 x 5 m bay open at the front, four posts, a
  top ring, a rear frame with three rails where the five shoulders sit, a console with a screen at the front right; 120 W;
  `"shellGantry": {"bay", "arms", "reach": 4}`), **Shell Parts Table** (a Piece Rack by `storage.pieces`, 1.2 x 2.4 m, 60 pieces up to
  1 x 0.95 x 2.2 m, a Piece in; `"shellGantry": {"table": true}`) and seven module parts (`Shells/Modules`: Shell Optics Head, Plated
  Head, Power Pack, Hand, Standard Leg, Heavy Leg, Runner Leg). Their Assembler recipes are appended at the end of the table
  (`Assembling.cs` `LateHand`: saved recipe numbers keep).
- **The table's link** (default, the user away): the table is linked to the gantry by being in its arms' reach: a Shell Parts Table
  whose hold's middle is behind the rear frame and within 4 m of all five shoulders, on the gantry's construct or standing on its own
  (set down on the ground behind it). No wire or conveyor is needed.
- **The swap** (`ConstructGantry.cs`, `GameShellGantry.cs`, `CActKind.Gantry`, `Msg.Gantry` in the slot of `RemovedLabour`; protocol 370).
  F on the gantry opens its panel (`UiShellGantry.cs`, `Program.SandboxShellGantry.cs`; Esc and the local reset close it): the shell
  in the bay, the table, and a row a slot with a button for each module that fits there and how many lie on the table. The server
  checks reach (8 m), power, who may, a shell standing alone in the bay (its feet inside it), the slot (`fixed`, kind, a part), the
  table and that the module is on it (creative: it comes free, with or without a table), takes it off the table and starts an 8.6 s
  job on the slot's arm (head arm: head; right upper: right arm and the back; left upper: left arm; the lower arms: the legs). The
  arm reaches the slot (1.2 s), unclips (to 2 s), carries the old module to the table (3.5 s), sets it down and takes the new one
  (4 s), carries it back (5.5 s), fits it (6.3 s: the shell's kit is written and the old module's part goes onto the table, else
  loose behind the gantry, always with a log line) and goes back to rest. A job stops, the new module back on the table, if the
  shell or the gantry goes or the shell leaves the bay before the fit. A shell in a job can't be woken. One job a gantry (since "Swap animation" below: several, queued, under a waist clamp, 8.6 s each); a
  joiner hears the jobs under way.
- **Seen** (`GantryRender.cs`): the five arms (shoulder, two links, elbow, gripper) at rest, and the working arm on its path with the
  module's part at true size in its grip, a work lamp while it unclips and fits; the same pose on host and client from the job's
  clock. The shell itself looks the same whatever its modules (not drawn yet).
- **Tried** (window runs, the window tool's `shellgantry setup|fit SLOT MODULE|state`): a Robot Shell's left leg, right leg and head
  swapped from the table, the old modules on the table after; a second ask while busy refused; worn after, the pace x1.10 and hits
  x0.90 (Runner Legs and the Plated Head); the Mining Shell's back and right arm refused as fixed, its leg swapped; a joined client
  sees the job, the kit and the table, and its own ask is made (and refused "too far away" when it was); a world save and load keep
  the kit and the table's contents; the panel opens on F and closes on Esc.
- **Not yet**: the standing shell drawn with its modules (the worn one is, 2026-11-19: "How a worn shell shows its modules" above);
  a crew Miner using the gantry; arm sounds.

## Swap animation (user, 2026-11-19)

"Add animations for the shell being disassembled and reassembled on the gantry. Add some locking mechanism that holds the torso in place."
Built 2026-11-19 (`ConstructGantry.cs`, `GameShellGantry.cs`, `GantryRender.cs`; protocol 377). Defaults chosen with the user away:

- **Waist clamp.** A ram from a housing on the rear frame's lower rail (between the lower arms' shoulders) runs out to a yoke behind the
  shell's hips, then two jaws on the yoke's ends swing in round the pelvis (10 cm deep, hazard striped outside), a tooth round each front corner with a lamp on its face, red while it grips (0.8 s: the ram the first 60%, the
  jaws the rest). The hips were chosen because the arms hang beside the torso and the Mining Shell's hopper sits on its back: the yoke
  passes under the hopper's floor. A lamp on the housing is red while shut, green open. It closes when a change's first swap begins and
  opens (0.8 s) when its last ends or is stopped. While shut the server holds the shell: `HeldBy` the gantry (no grab, nudge, edit
  or waking: "a Shell Gantry's clamp holds it while its arms work") and posed every tick where it stood in the gantry part's frame
  (`Game.GantryPin`), never frozen: a gantry aboard a moving ship keeps its shell riding the ship's frame (`Construct.GantryHold` lets a
  held shell ride), posed in that frame; beside a still gantry it is left asleep. Only a shell in a clamp is drawn part by part (the
  merged meshes leave it out while `GantryHold`).
- **One swap (8.6 s):** the arm reaches behind the slot (1.2), goes in and shuts its gripper (1.6), unbolts with a turning nut runner and a
  work lamp (2.2), pulls the module out along the slot's way off (head up and back, back pack back, an arm outwards, a leg back) (2.7),
  carries it to the table (4.2), sets it down beside the table's middle (4.5), moves over and grips the new one (4.9), carries it back
  (6.4), pushes it in (6.8), bolts it (7.4: the fit, as before) and goes home (8.6). From 2.2 to 7.4 the shell is drawn without that
  slot's boxes (head, visor and aerial; back pack; forearm and hand; thigh, knee, shin and foot: `ConstructGantry.BoxSlots`, classified
  by where each box sits in a person-sized shell); with a leg off the shell hangs in the clamp.
- **A change of several slots** is one request a slot, as before, but asking while the gantry works no longer refuses: it queues. A swap
  starts when the same arm's last one has ended, or when another arm's last one has fitted (that arm is only going home). A slot already
  asked is refused; a module needs one on the table for each queued swap that wants it. The panel lists the swaps "now" and "next".
- **Network:** each swap is a `Msg.Gantry` with its slot (an off names its swap) and its seconds run, negative while queued; every peer
  draws the clamp and the arms from those, nothing per frame on the wire. A joiner hears the swaps under way and sees the clamp shut.
- **Stops and saves:** as before, a swap stops (its new module stays on the table) if the shell or the gantry goes; the clamp opens when
  none is left. Swaps and clamps aren't saved (a world's clear drops both): a save mid-change writes the shell where it stands, loose,
  with the modules fitted so far, and the table with the rest.
- **Tried** (window runs, `shellgantry setup|fit|state`): a Robot Shell's left leg seen gone from the pull to the fit, carried, set
  down and the new one brought and fitted; the clamp's ram and jaws on a Robot and a Mining Shell, the lamp red then green; a head
  queued behind a leg ran after its fit; asking the same slot again refused; a joined client heard both swaps (the queued one too) and
  the kit after. Later: the gantry driven at 3 m/s turning (`shellgantry drive`) kept the clamped shell in its frame to the
  millimetre; a save mid-change loaded the shell loose with its old kit; the physics gun on a clamped shell was refused. Not tried: a
  part removal renumbering a gantry mid-swap, a client's view in a picture.
- **The module carried** is the shell's own slot (its boxes, in its paint: `GantryRender.DrawSlot`), so what leaves the shell is what
  the arm holds; set down, it stands on the table's floor; the new one's copy on the table is hidden while the arm holds it
  (`ConstructGantry.CarriedOff`, `RouteRender.DrawRacks`). The deck is light deck plate (look review).
- **Robustness:** a swap and its clamp know the gantry part by its place on the body, so a removal that renumbers the parts finds it
  again (the peers hear the swaps again under the new index) or stops the swap; a clamp never poses the shell in another part's
  frame. A joiner hears the swaps before the bodies come: the shell is marked held when it arrives (`ConstructGantry.Refresh`).
- **Not yet:** the old module set down on the table turns into the module part's own look at the fit; with a gantry moving and
  the shell not yet in its frame (a few ticks before the frame pass takes it), only the pin holds it against gravity (its contacts
  with the gantry are skipped as `HeldBy`).
