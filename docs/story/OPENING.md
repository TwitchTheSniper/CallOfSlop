# The survival opening

A new survival world starts with a story: you crash-land on the planet and pass out, come to for a moment with a recovery
team standing over you ("There's one over here!"), black out again and wake in the Ministry of Slop's penal colony. The
warden's induction (the keybind tutorial, `Source/Game/GameTutorial.cs`) takes over from there. Karl's idea (2026-11-07);
it leans towards premise E (Convicts) in the project doc `planets/PREMISES.md`.

Code: `Source/Game/GameSandboxOpening.cs` (the server's beats, the colony, the wire, the save), `Source/App/Program.Opening.cs`
(the camera, the screen and the skip key). Data: `Story/Opening/Opening.json` (names, times, lines), `Story/Casts/PenalColony.json`
(the staff), the `Penal Colony` blueprint (made by `tools/constructs/building_parts.py`, `blueprints()`).

## When it runs

- **A new survival world**: Survival chosen in the lobby (the same switch as the start kit), on a fresh world or an unsaved match.
  At the match start the server places the **Penal Colony** blueprint on the nearest clear ground round the parts rack (or the
  first spawn) whose ground varies by under 1.2 m, then the start kit round the colony (it is the colony's work yard), the
  **wreck** (`"wreck"`, the Crashed Plane: a hand-made blueprint, never a vehicle-kit vehicle) 70 to 140 m out, and adds the
  **Penal Colony cast** to the story, so the warden and three guards have bodies about the colony (their middle is just outside
  its gate). No room for the colony: the opening still runs, and everyone wakes near the start.
- **Every player, once per world**: the host, each friend and every late joiner, on first arriving alive. In a saved world
  "once" is the person's record (`WorldPlayer.Opened`); in an unsaved match, once per player id. Worlds from before this have
  no colony and no opening.
- **Skip**: the jump key, at any beat. Skipping puts them in the cell block at once and starts the induction.

## The beats

Times are in `Opening.json`. Each player has their own, and only they hear their scene's lines.

| Beat | What happens |
| --- | --- |
| Crash (3.5 s) | At the crash site. The view tumbles and shakes, fire flickers, a white flash, black. Caption. |
| Out (3 s) | Black. |
| ComeTo (8 s) | Lying on their back, eyes opening slowly and blinking, edges dark. Three of the recovery team (bots of the people's side, named in `"rescuers"`) stand over them, turned to them, and say their lines: "There's one over here!" and two more. |
| Under (3.5 s) | Eyes close. Caption ("Some time later..."). The team goes, and the player is moved to the colony's cell block. |
| Wake (4.5 s) | The black lifts on the cell block's ceiling, looking towards the warden; his line; they get up over the last second. Then the induction (`TutorialBegin`). |

While it runs the player lies prone and nothing they press acts (`OpeningFilter` on both sides, `OpeningInput` on the client),
apart from the skip key; the hands draw nothing and the start kit's notice waits until they wake. Killed during it, the opening
ends and the respawn is the start. Leaving during it, they are put in the cell block first, so they come back there.

## The seam with the tutorial

Two server calls on Game, both taking the player id: `TutorialDefer(id)` as the opening claims a player (on the tick they
arrive), and `TutorialBegin(id)` when they wake or skip. This file declares them as `partial void`; the tutorial implements
them. `Game.OpeningActive` (the local player is in it), `Game.OpeningWarden()` (his body, or null) and `Game.OpeningHas(id)`
(server) are there for the tutorial to read.

## Network and save

- **`Msg.Opening`** (protocol 199), server to one client, reliable: the beat (`OpeningStage`), the time into it, its length,
  the camera's focus, the caption and the line just said. Lines also go as ordinary chat lines from the speaker (over their head),
  to that player only. **`CActKind.OpeningSkip`** asks to skip.
- **The world save** has an `Opening` section (tag `OPEN`, its own section, so an older reader skips it): the map, the colony's
  construct id, the crash site, and the key and Steam id of everyone who has been through it. The colony itself is an ordinary
  anchored construct, saved with the rest. On another planetoid (free travel) the section's map doesn't match, so there is no
  colony there and no opening for someone joining there.

## Changing it

- Words and times: `Story/Opening/Opening.json` (ASCII, refused with a reason otherwise: then there is no opening).
- The colony: the `Penal Colony` blueprint in `tools/constructs/building_parts.py` (run it to rewrite the file). The cell
  block and the gate are found by their cells (`OpeningSpots`): moving them in the blueprint means moving them there too.
- The staff: `Story/Casts/PenalColony.json` (the warden's name is also `"warden"` in Opening.json).

## Later

- **Ranks** (Karl, 2026-11-07): a player can work their way up in the colony (inmate, then trusty, and on up) instead of only
  breaking out, as a standing ladder with the Ministry (faction standing, `Story/Factions`). A breakout stays possible: the
  colony is an ordinary construct with a gate, and the start kit stands outside its fence.
- Later in the Ministry's line, dropping an antimatter bomb on a town or colony makes a faction universally hunted.
