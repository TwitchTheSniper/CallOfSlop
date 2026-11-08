# The Orbital Depot

Karl, 2026-11-08: a player's first sight of space in the survival story is through a robot shell. After the warden's
induction (docs/story/OPENING.md, "The seam with the tutorial") the Ministry assigns them to remote labour: they walk to the
**Remote Works** beside the penal colony, sit in a **Shell Control Chair**, and wake in a robot shell on the **Orbital
Depot**, a Ministry station 1.5 km overhead. There they fetch a Driving Module, fit and link it to a lander, and fly the
lander down to the apron in front of the Remote Works, where their real body is waiting. The lander is theirs to keep:
their first spacecraft.

The shell itself (parts, waking, leaving, a lethal hit only losing the shell) is docs/sandbox/CONSTRUCTS.md, "Robot shells".
The depot's side is `Source/Game/GameDepot.cs`, its words `Story/Depot/Depot.json`, the panel `Source/UI/UiDepot.cs`.

## Why the depot hangs in the sky

The depot is an anchored construct in the colony's own world, `"height"` metres (1500) over the Remote Works' apron, not
on another planetoid. Free travel runs other planetoids as separate worlds, and a lone host can't send their shell to one
while their body sits in a chair on this one; nobody can stand in orbit either. Hanging it overhead keeps the beat in one
world, in co-op, with the colony in sight below. Moving it to a real moon later means a shell that can live in another
`Game.Place`.

## The run

- When a player's tutorial is finished or skipped, the server gives them the **report** state and the memo (as a line
  from Depot Control). The panel points at the Remote Works.
- Sitting in a Remote Works chair: if they own no shell that can be woken, one is stood in the depot's shell bay for them
  (`ShellWanted`). Waking in it moves them to the first step.
- Each step has a cue the host judges ten times a second while they wear the shell (`DepotJudge`):

| Cue | Done when |
| --- | --- |
| `wait` | `seconds` have passed on the step |
| `walk` | they are `amount` metres from where the step began |
| `reach` | they are within `amount` metres of a spot (`edge`: the deck's front edge) |
| `hold` | they hold the named part (or their lander already has one) |
| `fit` | their lander has a Driving Module |
| `link` | the lander drives (a seat linked to the module, the module to a jet) |
| `board` | they sit in the lander's driving seat |
| `clear` | the lander is `amount` metres below the depot's deck |
| `land` | the lander is at rest within `amount` metres of the apron |

- Each player has their own lander (the `Depot Lander` blueprint: the Orbiter without its Driving Module) in one of four
  hangar slots, and their own Driving Module on top of the stores tower. Up to four can run it at once; a fifth waits for a
  slot.
- A lander that is wrecked or lost sends them back to the `hold` step with a new lander and module. A shell lost while
  boarding, clearing or landing does the same. A shell lost earlier just wakes them in the chair; sitting again gives a new
  one.
- At the last step the lander becomes theirs, the link closes and they wake in their body (`DepotFinish`).

## Network and save

- **`Msg.Depot`** (protocol 210), server to one client, reliable: u8 state (0 none, 1 report, 2 + step, 255 done), u8
  whether there is a target, vec the target. The panel and the marker read it.
- **The world save**: the Opening section gains the Remote Works' and the depot's construct ids (an older reader stops
  before them), and a `Depot` section (tag `DEPO`) holds each person's state and lander id, onto their record. A world from
  before the depot gets one when it loads, if it has a colony.

## Changing it

- Words, the height and the cues: `Story/Depot/Depot.json` (ASCII; a bad file is refused with a reason and there is no
  depot).
- The buildings: the `Remote Works` and `Orbital Depot` blueprints in `tools/constructs/building_parts.py`. The spots the
  steps use (apron, shell bay, deck edge, stores, hangar slots) are local points in `GameDepot.cs`: moving things in a
  blueprint means moving them there too.
- The lander: `tools/constructs/shell_parts.py` writes `Constructs/Blueprints/DepotLander.json` from the Orbiter.
