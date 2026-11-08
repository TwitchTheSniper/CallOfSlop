# The warden's induction (the first tutorial)

After the story opening (crash, "there's one over here!", waking in the penal colony), the Ministry of Slop's warden walks a new
convict through the important keys and first survival steps, one step at a time, with a dry aside per step. Code:
`Source/Game/Tutorial.cs` (the data), `Source/Game/GameTutorial.cs` (server state, cues, network, save),
`Source/UI/UiTutorial.cs` (the HUD panel and the Esc menu's buttons). Sandbox only.

## The data: `Story/Tutorial/Induction.json`

Format `call-of-slop-tutorial/1`: a `speaker` (the warden's name) and `steps`, each with `id`, `title`, `text`, `warden`
(optional) and `cue`, plus `seconds` (wait), `amount` (look, move, sprint) or `tool` (tool). A bad file is refused with a
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
| `inventory`, `toolwheel` | the client reports the window opened (the only cues a client sends; the server takes them only for the current step) |
| `hold` | a part in their hands |
| `tool` | that tool out (`attach`, `linker`, `configure`, `grounddrill`, `physicsgun`, `hands`, ...) |
| `place` | an Attach or PlaceNew the server carried out |
| `weld` | a Join, freeform, socket or point weld carried out |
| `link`, `power` | a routed or world link laid (`power`: a power cable) |
| `drill` | the ground drill dug or filled |
| `assembler` | an Assembler's settings set, or F (Use, Bin) on one |
| `configure` | any part's settings set |

**Tools are issued as the steps need them** (Karl, 2026-11-08). While the induction runs the player has no gun and no tool but
bare hands: a step's `"gives": ["attach"]` (tool names as for `tool`) hands those over at that step and for every later one
(`TutorialStep.Issued`), and a `tool` step whose tool no step up to it gives refuses the file. `Tutorial.Gate` decides, the
same on the client (`SandboxToolGated` in `SandboxSend`: a line instead of a request) and the server (`CActKind.Tool`, and
each running tick, so a respawn's gun goes back to the warden): a tool not issued is refused, the gun or putting a tool away
gives bare hands. Finishing or skipping gives everything back.

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

## Network (protocol 201, the group 208)

`Msg.Tutorial`, a `TutorialOp` first, reliable ordered. Server to one client: `State` (u8 step, u8 state) whenever it changes
and when they arrive (late joiners included). `Group` (u8 they have done the step, u8 others in their group, u8 of them done, text who the group waits for) when their group changes. Client to server: `Cue` (u8 cue), `Skip`, `Replay`, `Resume`. The host's own
player goes straight to the server's methods and view, with no packet.

## Saves

Each person's place is on their world record (`WorldPlayer.TutorialState`, `TutorialStep`) and in a section of its own
(`WorldSaveWriter.TutorialTag`, written only when someone has started it), so an older reader skips it and a save without one
starts everyone fresh. Records are matched by key and Steam id, as factions are.
