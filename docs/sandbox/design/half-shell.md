# The Half Shell

The user (2026-11-20): "start work on the half-size player shell." A modular robot shell frame at half a person's size, added the way
the Mining Shell was added at 1.5x (mining-shell.md "Modular shells"): one more frame in `Constructs/Shells.json`, one more part from
the part kit, and the body-size rules that already take a frame's `scale`.

## What it is (defaults taken with the user away; each may be changed)

- **Frame 2, "Half"** (`Constructs/Shells.json`): part `Half Shell`, `scale` 0.5, `speed` 0.9, `damage` 1.3, `lift` 0.5, `sit` false,
  `jobs` `["crawler"]` (no crew job uses it yet). Its modules are the Robot Shell's: Optics Head, Power Pack, two Hands, two Standard
  Legs; no slot is `fixed`. Its fold is the Robot Shell's (lengths are a person's metres times the scale).
- **Size, collision, eye, camera.** All from `World.BodyScale` as for the Mining Shell, so the client predicts it exactly as the server
  runs it: capsule radius 0.2 m, standing 0.9 m (eye 0.8), crouched 0.6 m (eye 0.53), prone 0.3 m (eye 0.23). The first-person camera
  sits at that eye; hit boxes, player separation, construct blocking and the ground drill's reach (2.5 m) scale with it.
- **Headshots** now scale with the body (`World.IsHeadshot`'s `scale`, passed by the shot code): a Half Shell's head is over 0.7 m,
  a Mining Shell's over 2.1 m. Before, every body used a person's 1.4 m (a Half Shell could not be headshot standing; a Mining Shell
  was headshot in the chest). Blood and sound guesses still use a person's heights.
- **Step height and mantle stay a person's** (0.55 m), as the Mining Shell's do: a half-height robot that couldn't climb 0.4 m stairs
  would be stuck in every building. It hops up a step a person walks up.
- **Speed x0.9, hits x1.3** (thinner plating; its health is any shell's `World.MaxHealth`, so it is about 77% as tough). The part is
  12 kg, 80 hp, armour 2 (the Robot Shell's 38 kg, 160, 3).
- **Lift.** A frame's new `"lift"` (0.1 to 2, 1 if not said) is the share of survival's physics gun limit its wearer has
  (`ConstructEdits.LiftShare`, `Game.ShellLiftShare`; `LiftOf` multiplies): a Half Shell takes 1.5 t where a person takes 3 t, and
  fits modules by hand to half the mass. Creative is not limited. The pull (acceleration) isn't changed. The share is the server's;
  a joined client's own pre-check still uses a person's limit, so the server's refusal is what it sees.
- **Seats.** A frame's new `"sit"` (default true). A Half Shell has `"sit": false`: every seat refuses it on the server
  (`ShipSeatRefusal` first: "a Half Shell is too small for a seat: its controls are out of reach"). Why: the seated pose is a person's,
  and a 0.9 m body in it would sit with its eyes below a cockpit's sills and its hands short of the controls. The Mining Shell keeps
  sitting as a person does. The look line still offers "F: sit" (not changed).
- **Ladders** climb as for anyone (`World.Body` on the rungs): its capsule fits every shipped ladder shaft.
- **Gaps and low decks.** Nothing special: the capsule decides. Standing it passes under a 0.95 m deck, crouched under 0.65 m, prone
  under 0.35 m, and through a 0.45 m hatch or duct.
- **Tools.** It holds tools as a person does (two Hands). Third person, the rig and the tool in its hand are drawn at half size with
  the body (`PlayerRig`). First person the tool view model is not scaled (it is drawn at the eye), so a tool's screen reads as it does
  for a person; its reach is a person's except the ground drill's (times the size, as for every frame).
- **Modules: scaled, not a set of their own.** The Shell Gantry fits the same module parts on any frame, and each is drawn at its
  frame's size (`ShellFittedLooks.Place`, `ShellModuleNodes` on the scaled rig). Why: a module is one part a player owns and moves
  between shells; a half-size set would double the module parts and recipes for no rule that needs it. Tried: a Runner Leg and a
  Plated Head fitted to a Half Shell on a gantry (pace x0.945, hits x1.17 worn after). The gantry's arms reach down to its slots as
  they are; its waist clamp takes the frame's scale as before.
- **Pods.** 0.9 m is the Tiny class (1 m and below): it docks in a Tiny Shell Pod or larger. Folded it is about 0.48 m, under a Tiny
  pod's 0.55 m depth.
- **Getting one.** Survival: an Assembler recipe, tier 2: 12 steel, 4 copper, 2 rubber, 1 Circuit Board, 20 s (appended to the
  Assembling `Tail` list). Creative: the parts menu, Shells > Shells and chairs.
- **Wire.** Protocol 380: the part file moves part ids; frame 2's byte rides `MoveState.Frame` and `Msg.Shell` as any frame's.

## Tried (window runs, `--save-dir` a temp folder)

`shellpod setup` (a Tiny pod 7, a Half Shell on channel 3, its chair behind it): woke in it from the chair (eye 0.80, height 0.90,
radius 0.20); walked; crouched (0.60) and prone (0.30); a seat refused it; docked in the Tiny pod by its button and the pod held it.
`shellgantry setup Half Shell`: left leg and head swapped. With a joined client wearing it: the host saw it at half size standing,
crouched and prone, and the client's first person beside a person; the client docked it in the Tiny pod.

## Not yet

- Lift and low-deck passing were not tried by hand; nor a Half Shell climbing a ladder or taking a headshot.
- A crew job for it; a seat sized for it (a "small seat" whose part allows `sit: false` frames).
- The look line's "F: sit" for a frame that can't sit.
