# Shell storage pod (design, user 2026-11-18)

A place to park a robot shell when nobody wears it. Drafted with the user by clickable questions and two pictures; Approved to build (user, 2026-11-18): build it all after the Shell Gantry merges (both change Shells.json). The Shell Gantry (mining-shell.md) is a separate station.

## The user's answers (2026-11-18)

1. **Purpose:** park and wake. A shell stands docked in its pod when not worn, and its wearer wakes into it from a Shell Control Chair.
2. **Size:** one shell per pod; several pods side by side make a rack.
3. **While docked the pod:** charges the shell from the grid; empties a Mining Shell's hopper into a linked bin or conveyor; protects it (it can't be damaged or taken while docked).
4. **Docking:** the pod sits flush with the surface it is mounted on. As a worn shell approaches, a frame rises out of the pod. The player walks into the frame and presses a button on it: they are returned to their control chair. The frame then retracts, drawing the shell into the pod as the shell folds in on itself, compacting as small as it can, so the closed pod is smaller than a standing shell (about half its height). Waking into a docked shell reverses it: the frame rises, the shell unfolds and stands in the frame.
5. **Mounts:** three orientations, floor, wall and ceiling, each in two styles the player chooses when placing: **flush** (the pod body recessed into the surface, lid level with it) and **surface mount** (the pod body bolted onto the surface). The frame extends out of the pod along the surface's normal: up from a floor, out from a wall, down from a ceiling (the folded shell is drawn up into a ceiling pod). Surface mount on a floor replaces the earlier freestanding idea. Preferably one modular pod core part placed on any face, flush or surface; a few fixed variants are acceptable if simpler (user, 2026-11-18).
6. **Design first:** the user wants to see a design of the fold and the pod before the model is built.

7. **Sizes by the shell's standing height:** Tiny (1 m and below), Small (2 m and below: the Robot Shell, 1.8 m), Medium (5 m and below: the Mining Shell, 2.7 m), Large (15 m and below) and Huge (30 m and below). Build Tiny, Small and Medium now; Large and Huge later. A shell docks only in a pod of its size class or larger.

## What was built (2026-11-18)

- **Parts** (part kit `shell_parts`, `Constructs/Parts/Shells/Pods`): six, a pair per size: **Tiny / Small / Medium Shell Pod** (bolted
  on: a flange and corner bolts, a status lamp and a vent on the front) and **Tiny / Small / Medium Flush Shell Pod** (the body below its
  socket, sunk into the floor, wall or ceiling it is welded to, the lid at its solid's top 3 cm over the surface in a hazard coaming). One `bottom` socket each, so either goes on any
  face; flush or bolted is chosen by which part is placed (one core part with a placing option would have needed solid boxes that
  change with the option: a flush body overlaps the surface it sinks into, which the build refuses). Footprints 0.7 / 1.24 / 1.9 m,
  depths 0.55 / 1.0 / 1.5 m (about half the class's shipped shell). Power 30 / 80 / 150 W. `"shellPod": {"class", "half", "top",
  "depth", "flush"}` (`ConstructShellPod.cs`). Recipes appended at the end of the Assembler's table (`Assembling.cs` `LateHand`).
  Protocol 371 (part ids move; `Msg.ShellPod` in the slot of `RemovedLabourHands`; `CfgKey.PodShell..PodOreUnits`).
- **A flush pod is a pocket** (the user, 2026-11-19: it looked as if the shell clipped through the floor). The surface it sinks into
  is not drawn over its opening. Each frame the flush pods nearest the eye (at most 16 within 60 m) give their pockets
  (`ConstructShellPod.Pocket`: inside the rim, from the lining up to 1 mm under the lid's top, so the surface's seams go but what
  stands on the lid doesn't) as openings, grouped by body (`ConstructRender.GatherPocketCuts`, `Renderer.Cuts.cs`). The boxes of a
  body with openings carry a cut mark naming its own run of them and are drawn by the `CUTS` variant of the instanced and merged
  box programs (one at a time for shapes, blended boxes and glass); every other draw keeps the plain programs, which have no
  discard. No pod box is in its own pocket; its lid, frame and shell are drawn after, unmarked. Inside: plating a step lighter
  than the lid, a tread-plate floor, a cyan trim on the rim and a status lamp on its corner (green empty, amber docked or at work,
  red broken). The lid is two flaps at the solid's top inside a coaming 3 mm higher; they swing down against the walls over the
  first 30 % of the frame's rise, then the frame's head comes up between its posts (`ShellPodRender.Flaps`). Render only: no wire
  or save change. Not cut: the headless recorder's draws.
- **Fits**: a shell's class by its standing height (1.8 m x the frame's scale); it docks in a pod of its class or larger (an assumption
  of the rule "docks only in its size class or larger").
- **The frame** (`ShellPodRender.cs`): the lid rises on two posts along the face's normal (the button panel on the right post, a lamp on
  each post's head) and a platform rises from the pod's floor to the lid. An empty pod's frame rises as a worn shell that fits comes
  within 6 m (fully at 3 m), from the players every peer knows, so a joined client sees it without a message.
- **Docking**: in a shell, stood in an empty pod's frame (beside a bolted-on floor pod counts: a 1 m pod is past the step height), the
  look line names the pod and the use key presses its button wherever the crosshair is (`Program.SandboxShellPod.cs`, `CActKind.Use`
  on the pod). The server checks the frame, the class, the pod free and whole, who may (the Robot Shell Dock's rule), sends the wearer
  back to their chair (`ShellReturn` with the pod) and keeps the shell in the pod's hidden settings: frame, channel, owner, wear,
  damage, its modules and a Mining Shell's hopper load. A 3.5 s job (`Msg.ShellPod` to every peer and each joiner) slides the shell
  from where it stood into the frame and folds it (1.8 s), then the frame draws it in.
- **The fold** (`Shells.json` `"fold"` per frame, `ShellFold`): it kneels (hip 90, knee -160), bends at the waist, draws its arms in,
  sinks its head and drops; about half its height. The Mining Shell keeps its back straighter (bend -15, the hopper upright), its
  hand in its lap and the drill arm laid forward across its knees (right shoulder 80, elbow 0). Each box of the shell's part is turned
  about the joint of the limb its middle is in. A ceiling pod hangs the folded shell by its top; a wall pod lays it back into the wall.
- **Waking**: a Shell Control Chair finds a docked shell on its channel like any other (`ConstructShell.Find`, the pod's stored owner);
  the pod's 3.5 s wake job raises the frame with the shell folded (1.5 s) and unfolds it; at the end the server stands it as its part,
  as kept, and the sitter wakes in it (`ShellEnter`). Stood up from the chair before then, the frame draws the shell back in.
- **Where it stands**: upright on the lid of a floor pod; under a ceiling pod with its head at the lid (it drops to the floor if the
  room is taller); in front of a wall pod, its back to the wall, its middle level with the pod's (mount a wall pod at the shell's
  waist height: 0.9 m for a Robot Shell, 1.35 m for a Mining Shell).
- **While docked**: the power cell charges while the pod has power (wear down 2 a half second: flat to full in about a minute; the
  other works are a service's, not a charge's); a bolted-on Medium pod is a 5,400 unit bin and takes a Mining Shell's hopper load at
  120 a second, its Chute out taking the ore on (a flush pod has no bin: its body is in the surface, so its Mining Shell keeps its
  load); the shell is no part, so nothing can hit it or take it; a pod holding one can't be picked up, and a pod taken off, broken or
  wrecked stands its shell at its frame first (`ConstructShellPod.Release`, the next pod tick). Blueprints never carry a docked shell.
- **Saves**: the pod's settings ride world saves and CBody; a world saved during a job loads with the shell docked. Older saves have
  no pods.
- **Tried** (window runs, the window tool's `shellpod setup|go N [other]|dock|state`): a Robot Shell docked by F and woken from a
  bolted floor pod and a bolted ceiling pod; a Mining Shell with 500 iron ore docked in a flush floor pod and a flush ceiling pod and
  woken with its load; a save and load with two shells docked, then a wake; with a joined client: the client docked by its own F,
  saw the fold and the wake, and woke again; a power cell charging; a hopper emptied into a Medium pod's bin.
- **Not yet**: Large and Huge pods; the docked shell's charge shown on a joined client before it wakes (it hears the wear when it
  wakes); the drill arm and the hopper drawn by their modules (the fold turns the part's boxes); a sound for the frame.
