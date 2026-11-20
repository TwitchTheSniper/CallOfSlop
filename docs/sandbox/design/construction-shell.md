# The Construction Shell

The user (2026-11-19, manipulators.md): "In survival, instead of a physics gun with upgrades, I would like instead there to be robotic
arm stations or shells that are made for manipulating large parts. In queue, a new 5-meter construction shell that has the ability to
use a welder and the ability to pick up and move pieces." The survival physics gun stops at 3 t; this shell is the first thing that
moves the bigger pieces. Added as the Half Shell was (half-shell.md): one frame in `Constructs/Shells.json`, one part from the part
kit, and the body-size rules that already take a frame's `scale`.

## What it is (defaults taken with the user away; each may be changed)

- **Frame 3, "Construction"** (`Constructs/Shells.json`): part `Construction Shell`, `scale` 2.8, `speed` 0.75, `damage` 0.6 (heavy
  plating), `lift` 5, `sit` false, `jobs` `["builder"]` (no crew job uses it yet). Head Optics Head, back Power Pack, left arm
  **Welder Arm**, right arm **Grip Arm**, both legs Heavy Leg. A frame's `scale` may now be 0.5 to 3 (was 2.5) and `lift` 0.1 to 10
  (was 2).
- **Size.** 2.8 x a person: standing 5.04 m (eye 4.48 m), crouched 3.36 m, prone 1.68 m, capsule radius 1.12 m. All from
  `World.BodyScale`, so a joined client predicts it as the server runs it; hit boxes, headshots, player separation and construct
  blocking scale with it. Step height and mantle stay a person's (0.55 m). 2.8 rather than 2.78 (exactly 5.0 m) on purpose: see Pods.
- **Lift: 15 t** (`"lift": 5` times survival's 3 t, `ConstructEdits.LiftOf`). Why 5: the user's intent is that big pieces move
  by a manipulator you can see, and the 3 t cap now refuses most structural pieces; 15 t takes every Large plate, deck, girder,
  column and dome of the capital kit (10 to 14.4 t) and a 15 t truck, while a capital hull block or a whole ship stays for cranes,
  gantries and arm stations (the robotic arm stations the user also named). Creative is not limited, as before.
- **Pull.** A frame's lift share above 1 also multiplies the beam's pull (`ConstructEdits.PullOf`): 150 kN x 5 = 750 kN at the base
  Lift level, so 15 t moves at 50 m/s2, as a person's 3 t does on 150 kN (a Lift Coil raises both alike). The Half Shell's 0.5 keeps
  the person's pull.
- **Reach.** A frame larger than a person reaches that much further with its attach tool and its physics gun
  (`ShellFrame.Reach` = max(1, scale); `ConstructEdits.ReachShare`, `BuildReachOf`): the attach tool's 4.5 m becomes 12.6 m
  and the beam's 12 m survival reach 33.6 m. From an eye 4.48 m up a person's reach barely meets the ground. Both sides read it:
  the server's `AttachReach`, `WeldReach` and line building, the client's aim ray, ghosts and socket-weld markers. Other tools
  (paint, remover, configure, wires) keep a person's reach: the shell has only its two arms' tools anyway.
- **Arms: both fixed** (`"fixed": ["leftArm", "rightArm"]`). The Grip Arm is its physics gun, the Welder Arm its attach tool; with
  tool arms only those tools are offered (mining-shell.md "Tool arms"). They are the same modules a Robot Shell can wear, drawn at
  the frame's size (`ShellModuleNodes`), so no separate "heavy" modules: the frame is what lifts 15 t. Why fixed: no Shell Gantry
  holds a 5 m shell (`GameShellGantry.GantryTallest`, 3 m: "too tall for the Shell Gantry"), so a swappable slot could never be
  swapped; a gantry its size would change that.
- **Grip holds while welding.** A worn shell with a physics gun arm and a welder arm (`ShellFrame.GripWelds`) that switches from
  the gun to the welder while holding a piece freezes it where it is (`ConstructGrab` GrabTick: the physics gun's own Freeze)
  instead of dropping it: "the grip holds it where it is: weld it on". The welder's socket weld then moves the frozen piece onto
  the build (a frozen body is a piece that moves: `SocketWeld.PieceRefusal`). A Robot Shell fitted with both arms on a gantry
  does the same. RMB while holding still freezes as before.
- **Welding at its scale.** The weld rules are the welder's, with the reach above; a big piece's fit time is its part's attach
  seconds (a Large Girder 12 m: 8 s, LMB held).
- **Seats.** `"sit": false`: every seat refuses it, "a Construction Shell is too big for a seat" (the Half Shell keeps "too small").
- **Doors and decks.** Nothing special: its capsule decides. Crouched it is 3.36 m and 2.24 m wide, so it passes no shipped door
  (the tallest is 2.25 m) and no ship deck; prone, 1.68 m. It works outside, on open decks, in hangars and on build sites. Ladders:
  no shipped shaft is wide enough ("no room").
- **Pods.** None now. At 5.04 m it is the **Large** class (over 5 m, to 15 m): "a Construction Shell (5 m) needs a Large pod or
  larger", and Large pods aren't built. Why not Medium: folded it is 0.95 x 2.8 = 2.66 m high, and a Medium pod is 1.5 m deep; it
  would stand a metre out of the pod. A Large pod, when made, takes it with no change here.
- **Camera.** First person, the eye at 4.48 m; the arm's view model is scaled with the frame, so it reads as a person's tool. Third
  person on foot, the distance behind and the shoulder offset are times the body's size when larger than a person
  (`ThirdPerson.Size`), so the camera sits 8.4 m behind instead of inside the body.
- **The part.** `Constructs/Parts/Shells/ConstructionShell.json` (part kit `shell_parts`, `ShellParts.ConstructionShell`): the
  robot at 2.8x with the Grip Arm's and Welder Arm's looks in place of its forearms (`ArmOnBody` reads the module looks, now named
  methods `GripArm`/`WelderArm`, so the module files are unchanged), hazard flashes beside the chest plate. 900 kg, 600 hp, armour 8.
- **Getting one.** Survival: an Assembler recipe, tier 2: 220 steel, 30 copper, 16 rubber, 6 Circuit Boards, 120 s (the last row of
  the Assembling `Tail`). Creative: the parts menu, Shells > Shells and chairs. Wake in it from a Shell Control Chair on its channel.
- **Wire.** Protocol 382: the part file moves part ids; frame 3's byte rides `MoveState.Frame` and `Msg.Shell` as any frame's.
- **Cost per tick.** Two delegate calls a tick for a holder (reach, and the grip check only while the welder is out with a piece
  held); nothing allocates.

## Tried (window runs, `--survival`, `--save-dir` a temp folder)

Window verbs `cshell` (host: a Construction Shell on channel 4, a Robot Shell, its chair and a frozen Large Column 8 m), `cshell look`
(the look line and the weld step) and `aimsock PART SOCKET` (the look on a socket, for the welder's markers).

- Host: woke in it from the chair (frame 3, eye 4.48, height 5.04, radius 1.12, pace x0.75); grabbed a 12 t Large Girder 12 m with
  the Grip Arm and lifted it to 8-9 m; switched to the welder: frozen in the grip; socket-welded its end onto the column from 8 m
  (8 s held): "SocketWeld ok ... on the grid".
- Joined client: the same chain over the network (woke, lifted, the grip froze the piece on the switch, welded 12 t on); the host
  saw the client's shell at its size with its arms.
- Shots for a look review: `/tmp/cs-shots/` (beside a person and a Robot Shell, lifting in third and first person, a joined client lifting).

## Not yet

- Walking it about at length, crouching and prone, ladders and doors; a headshot; a Large pod.
- A Shell Gantry its size (both arms are fixed until then), a crew builder job.
- Robotic arm stations (manipulators.md): the other half of the user's direction.
