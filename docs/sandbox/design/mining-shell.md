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
