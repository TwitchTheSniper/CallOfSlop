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
- **The ore hopper** (back slot). The Mining Shell's part is a bin: `storage.bulk` with 900 units of one material at a time,
  ores, stone and sand only (`takes`). Standing, it is a bin like any other: F takes a load out, its look line shows the fill,
  and its load adds mass. Worn, the hopper's contents go with the player (`ShellRun`, `SandboxHands.Hoppers`) and come back
  into the part when it stands again, so nothing is lost by leaving.
- **The fixed drill arm** (right-arm slot). The right arm is a drill, always: it can't be put away or swapped. With the ground
  drill out (the tool wheel, as ever; the robot has it whatever the player owns), a scoop does what the ground drill does
  (dirt, stone and sand into the inventory, the hole in the ground) and, over an underground deposit the drill arm's tier
  works, also taps the deposit through `ResourceField.Tap` into the hopper: `units` a scoop (6, one scoop every 0.3 s: 20 a
  second, a Workshop Drill Module's rate). The spot rules are the Drill Module's: a 3 m spot gives 1,200 then says "this spot
  is worked out". Tier 2: iron and copper, not gold (tier 3) or levite (tier 4, deep). A full hopper or another material says
  so and the scoop still digs. The left arm holds every other tool as a person's hands do.
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
- **modules**: what can go in a slot. Each has a `name`, the `slot` kind it fits (`head`, `back`, `arm`) and a `kind` the code
  knows (`head`, `pack`, `hopper`, `hand`, `drill`), plus that kind's numbers (a drill's `units` and `tier`, a hopper's
  `takes`). Today's modules: Optics Head, Power Pack, Ore Hopper, Hand, Drill Arm.
- **frames**: a shell body. Its `name`, the `part` that stands as it, `scale`, `speed`, `damage`, `jobs`, the module in each
  slot (`slots`) and which slots are `fixed` (can't be swapped by customisation: the Mining Shell's drill arm and hopper).
  Frame 0 is the Robot Shell (Standard), described in the same layout with exactly its old numbers, so nothing about it
  changed. A frame's index is its byte on the wire (`MoveState.Frame`, `Msg.Shell`); a new frame goes at the end.
- **The code reads modules, not frames.** The drill rule asks "is there a `drill` module in an arm slot", the hopper "a `hopper`
  in the back slot", the renderer draws each slot's module. So customisation only adds modules (data, a look and, for a new
  kind, its rule) and a way to choose them (UI, and a per-shell record of the choice in the part's config).

## Questions for the user

1. **Size 1.5x everywhere, including the collision?** Default: yes, so it doesn't fit through doors standing and ship decks are
   crouch-only. The other choice is drawn 1.5x but a person's collision, which goes everywhere a person goes.
2. **Drill arm controls.** Default: the ground drill tool's controls (LMB scoops), with ore going to the hopper. The other choice
   is a key of its own so the drill works whatever tool the left arm holds.
3. **Numbers**: 900 units hopper, 20 a second at tier 2, x0.85 speed, x0.7 damage, the recipe. Placeholders until played.
4. **Customisation's rule**: may a slot's module be swapped anywhere (the attach tool at a standing shell, like the spares) or
   only at a station? Not built; the data's `fixed` list is ready for either.

## Not yet

- A crew Miner job (none exists) and factions spawning Mining Shells.
- The seated pose, a first-person view of the drill arm (the third-person rig draws it), drill sounds of its own.
- Tiers of drill arm (data: a second `drill` module with another `tier`).
