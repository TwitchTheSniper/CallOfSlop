# The warden's induction (the first tutorial)

After the story opening (a placeholder card for the crash, then waking in the penal colony), the Ministry of Slop's warden walks a new
convict through the important keys and first survival steps, one step at a time, with a dry aside per step. Code:
`Source/Game/Tutorial.cs` (the data), `Source/Game/GameTutorial.cs` (server state, cues, network, save),
`Source/UI/UiTutorial.cs` (the HUD panel and the Esc menu's buttons). Sandbox only.

## The data: `Story/Tutorial/Induction.json`

Format `call-of-slop-tutorial/1`: a `speaker` (the warden's name) and `steps`, each with `id`, `title`, `text`, `warden`
(optional) and `cue`, plus `seconds` (wait), `amount` (look, move, sprint) or `tool` (tool), and optionally `target`, `hint`,
`hintAfter` and `drops` (below). A bad file is refused with a
reason (logged as `[tutorial] refused: ...`) and the game simply has no induction. Player text is ASCII.

`{Action}` in `text` is a `GameAction` name (Keybinds.cs) and shows as the player's own key, `[Tab]`; an unknown name refuses
the file. The text is filled once per step and again only if a key it names is rebound.

| Cue | Done when (the server sees it) |
| --- | --- |
| `wait` | `seconds` pass |
| `look` | the view turns `amount` radians (default 3) |
| `move` | they walk `amount` metres (default 6; a teleport or a ride doesn't count) |
| `sprint` | `amount` seconds sprinting while moving (default 1) |
| `jump`, `crouch` | the key in their input, or rising / a lower stance |
| `inventory`, `toolwheel` | the client reports the window opened (the only cues a client sends; the server takes them only for the current step). The shipped induction has no `inventory` step: it runs in survival, where the Tab parts menu is off away from a beacon |
| `hold` | a part in their hands |
| `tool` | that tool out (`attach`, `linker`, `configure`, `grounddrill`, `physicsgun`, `hands`, ...) |
| `place` | an Attach or PlaceNew the server carried out |
| `weld` | a Join, freeform, socket or point weld carried out |
| `link`, `power` | a routed or world link laid (`power`: a power cable) |
| `drill` | the ground drill dug or filled |
| `assembler` | an Assembler's settings set, or F (Use, Bin) on one |
| `configure` | any part's settings set |
| `stow`, `draw` | a part put in the backpack (Stow), or taken out of it (Draw) |
| `carry` | the physics gun took hold of something (a Grab carried out) |
| `bunk` | they lie in a Bunk (the seat loop, beside the checkpoint: `TutorialSee`) |
| `ride` | they rode the crew bus (a stand-in, docs/story/DUTIES.md) from the step's `spot` stop (`colony` or `quarry`) to the other: `QuarryRideStep` |

**The mine shift** (Karl, 2026-11-11: woken up, shouted onto the bus, taken to the mine, the hands there, supports on the mine
wall, lamps; "show, don't tell"). The shipped induction is twelve short steps: `wake` (the warden shouts; look), `bus` (walk
and run to the crew bus, ride to the quarry), then Foreman Grubb at the mine face: `jump`, `hold` (a Lamp), `backpack` (stow
it), `physicsgun`, `carry` (a lump of Rubble comes off the face: shift it), `prop` (pick up a Pit Prop), `attach` (the tool),
`support` (fit the prop on the face's footing), `lamp` (draw the Lamp, fit it on the prop), `done`. A step's `text` is one
short line (the keys), the speaker's line carries the world, and the detail waits in the `hint`. The quarry duty follows on
at the works, and ends with the bus home and the bunk. Per step: `speaker` (who says the line and hands things over: default
the file's), `spot` (`colony`, `quarry`, `wall`: a marker the server places, sent in `Msg.Quarry`; a ride's marker moves to
its bus), `dropNote` (the notice when the drops land). The Mine Face (with five `prop` sockets on its footing), the Pit Prop
and the Rubble are parts in `Constructs/Parts/Story`; the face is part of the Ministry Quarry Works blueprint. A world saved
part way through the older induction picks up at the step now at that number (at the colony: the markers show the way, or
Esc skips).

**Play areas** (Karl, 2026-11-12: after the crew bus drops you at the mine, the mining part happens inside a set area; go out
of it and you are asked to go back, and nothing done out there counts). A step's `"area"` (`colony` or `quarry`) keeps it in
that place: the induction's `wake` and `bus` are the colony's, the mine shift the quarry's, and the quarry duty
(Story/Duties/Quarry.json) has the same field (its works and the ride home are the quarry's, the bunk the colony's). The areas'
words and sizes are `Story/Tutorial/Bounds.json` (format `call-of-slop-bounds/1`): each is a circle across the ground round
its place (the colony's middle, the works' centre), as wide as the construct or its bus stop, whichever reaches further, plus
`margin` metres. On the host, ten times a second (`PlayAreaTick`, Game/GameTutorialBounds.cs), someone on such a step who goes
past the edge is told by the Ministry's tannoy (a chat line from `speaker`, one of the area's `lines`, another every `repeat`
seconds while they stay out), the orange marker points to the area's bus stop, the panel says OUT OF BOUNDS, and the step
waits: its cue isn't judged, and actions carried out out there (`TutorialSawAct`, `QuarrySawAct`, a bunk) don't count. They
are back once `slack` metres inside the edge (the `back` line). No area applies while the bus has them or has just set them
down, or once they have done the step and wait for their group. A world with no colony or quarry has no bounds. Wire: the
out flag is a u8 at the end of `Msg.Quarry` (protocol 258).

**About the person** (Karl, 2026-11-10). The induction teaches the person: moving, their hands, the backpack, their own tools
(attach tool, ground drill, physics gun) and their bunk. Machines (power, the Assembler, smelting) are not in it: they are
taught later, in stages, on Ministry of Slop duties (the first is the quarry: docs/story/DUTIES.md). The power and Assembler steps were taken out on 2026-11-10, so a world
saved part way through an older induction picks up at the step now at that number.

**Markers, hints and drops** (2026-11-10). `"target": "loose"` or `"bunk"` puts an orange marker over the nearest loose single
part (the step's `drops` first, within 60 m) or free Bunk (within 200 m), with a line saying how far it is; each peer finds it
from the constructs it has, twice a second, nothing allocated (`Ui.TutorialLook`). `"hint"` (with `{Action}` keys) shows under
the warden's line once the step has been up `hintAfter` seconds (default 35). `"drops"` (at most four part names) are set down
in a row two metres in front of the person as the step starts, theirs like a part they dropped (`TutorialDrop`): the `hold` step
drops two Lamps and `weld` one more, so the build steps never lack a part, and `carry` drops a Frame Block for the physics gun.
A part the hands steps drop must be an item (not physics-only: welder-fitted, over 40 kg or bulky, which F can't pick up) and
fit a backpack slot (the `backpack` step stows it); a Frame Block is structural, so it only suits the physics gun. The penal colony has two Bunks down the yard's
west side (the open-air dormitory, `--gen-parts building_parts`) for the `bunk` step.

**Tools are issued as the steps need them** (Karl, 2026-11-08). While the induction runs the player has no gun and no tool but
bare hands: a step's `"gives": ["attach"]` (tool names as for `tool`) hands those over at that step and for every later one
(`TutorialStep.Issued`), and a `tool` step whose tool no step up to it gives refuses the file. `Tutorial.Gate` decides, the
same on the client (`SandboxToolGated` in `SandboxSend`: a line instead of a request) and the server (`CActKind.Tool`, and
each running tick, so a respawn's gun goes back to the warden): a tool not issued is refused, the gun or putting a tool away
gives bare hands. Finishing or skipping gives everything back.

**Story mode starts without the backpack** (Karl, 2026-11-08). Through the opening and the induction a survival player has no
backpack until the step whose `"gives"` lists `"backpack"` (the `backpack` step, after `hold`: stow the part, draw it back);
`TutorialStep.PackIssued` carries it to every later step, and finishing or skipping hands it over too. `TutorialPack` decides
on the server each tick (`ConstructEdits.IssuePack`): a pack held back has no slots, and that is how every peer hears it (the
pack message carries its slot count, so the protocol is unchanged; a newcomer is sent every pack without one). With no slots
nothing goes in (a part given goes to the hands or drops), none is worn or drawn, and Stow says it hasn't been given yet. Only an
empty pack is ever held back, so a replay or an old save loses nothing, and a death keeps or drops what is in it by the
world's death rules as before. Each person is judged alone, so in co-op everyone gets theirs at their own step. Creative has
no backpack anyway; the self-test's `TutorialAutoStart = false` leaves packs alone.

The movement cues read the person's input (`Player.LastInput`); the host's own player has it set where its input is sampled
(`Game.ServerTick`), as a client's is in `ApplyCmd`. Before 2026-11-08 it wasn't, so walk and run never completed for a host.

A step stays up at least 1.2 s, so one already done shows "Noted." rather than flickering past. Action cues come from
`SandboxAct`: after an action succeeds, `TutorialSawAct` reads its kind (and body and part where it needs them) from a copy
of the packet; nothing is allocated.

## Together (co-op, 2026-11-08)

People on the same step go through the induction as a **group**: friends who woke in the colony together, everyone in a saved
world loaded on the same step, or someone who starts, resumes or replays it while others are on that step. Each person's cue is
still judged on them alone, but the step moves on for the whole group at once: when everyone in it who is alive has done it, or
`TutorialGroupWait` (45 s) after the first one did, so a friend away from the keyboard can't hold the rest up (they are told the
warden moved on without them). Tools are issued to everyone in the group at the step that gives them. Skip is per person: they
leave the group and the rest carry on. Someone with no group (a late joiner on step 1 with nobody else there) has one of their own.

The panel shows "with N others", then "Done. Waiting for Ann, Bob." once they have done the step, or "2 of 3 done: the warden is
waiting for you." Server state per person: `_tutGroup`, `_tutDone`; each group is judged once a tick from its lowest id
(`TutorialGroupTick`), nothing allocated; the names are built only when the group changes.

## Who starts it, and the opening's seam

- The server keeps each person's step and state (`TutorialState`: None, Running, Finished, Skipped) by player id.
- A new person (state None) in a **survival** world starts it 3 s after arriving, alive and in play.
- The story opening (Source/Game/GameSandboxOpening.cs) calls `TutorialDefer(id)` when it claims a player, so it doesn't start
  by itself, and `TutorialBegin(id)` when they wake in the colony (or skip the opening). `TutorialBegin` does nothing for someone
  who finished or skipped it in this world.
- In creative it never starts by itself; the Esc menu starts it.
- The Esc menu (Sandbox): **Skip the induction** while it runs; **Resume** a skipped one where it stopped, or **Start it again**;
  **Replay the induction** once finished.

## Pacing (`Source/Game/GamePace.cs`, 2026-11-13)

How long the induction and the first duty take, so a change to them is judged on how it plays, not only on how it looks.
Two kinds of number, both `[pace]` lines, neither on the wire or in a save:

- **Measured, from real play.** When a person finishes a step the server logs `[pace] induction bus: 95.3 s (Name)` (the
  quarry duty's steps are `[pace] duty power: ...`), and at the end of each `[pace] induction done: 12 steps in 6 min 41 s,
  the slowest bus at 95 s`. They are in `callofslop.log` after any play session, the host's for everyone. A step's time
  is the person's own, from the step's start to their finishing it: the wait for the rest of a group is not in it, and a
  person the group moved on without gets `not finished, the group moved on`. A skip logs nothing, a replay starts the
  sums again, and steps done before the session started aren't in the total.
- **Estimated, with no player.** The window script's `pace` action (`--window-shot ... --map Glacier --survival --do "skip;
  wait 120; pace"`, and the play-test tour runs it) prints each step's floor: its words read at `PaceWordsPerSecond` (3 a
  second), the way on foot to the step's place at walking speed (with the sprinting time beside it), the bus (12 s to come,
  `QuarryRideSeconds` to ride) and a wait step's seconds, then a total per track and the distances between the places
  (cell block, gate, the two bus stops, the mine face). Distances are straight lines, through the gate when a step leaves
  or enters the colony. What the player does at a step (finding the part, fitting it) is not in the floor.

Reading them: a step whose measured time is several times its floor is where people are lost, waiting or stuck; a step
whose floor is mostly walking or the bus is dead time to shorten or to fill; reading that is more than a third of a
track's floor is too many words. Change `Story/Tutorial/Induction.json` or `Story/Duties/Quarry.json`, run `pace` again
and compare.

## Network (protocol 201, the group 209)

`Msg.Tutorial`, a `TutorialOp` first, reliable ordered. Server to one client: `State` (u8 step, u8 state) whenever it changes
and when they arrive (late joiners included). `Group` (u8 they have done the step, u8 others in their group, u8 of them done, text who the group waits for) when their group changes. Client to server: `Cue` (u8 cue), `Skip`, `Replay`, `Resume`. The host's own
player goes straight to the server's methods and view, with no packet.

## Saves

Each person's place is on their world record (`WorldPlayer.TutorialState`, `TutorialStep`) and in a section of its own
(`WorldSaveWriter.TutorialTag`, written only when someone has started it), so an older reader skips it and a save without one
starts everyone fresh. Records are matched by key and Steam id, as factions are.
