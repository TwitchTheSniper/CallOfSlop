# The survival opening

A new world on a start with an opening (docs/story/STARTS.md) begins with a short card, then everyone wakes where the start
puts them: aboard the start's ship in orbit (the Rust Bucket, the Magpie, the Zero Day: "A ship start" in STARTS.md), or at
the start with the opening's wreck (when it names one) lying out past them. Until 2026-11-17 the first opening was the
penal colony's (a crash card, waking in the colony's cell block, then the warden's induction); that was removed with the
colony, and its old doc is docs/archive/colony/OPENING.md.

The card is a placeholder (Karl, 2026-11-12): the falling-down-in-your-ship scene was taken out to be made again. The
unused beats keep their bytes in `OpeningStage` (wire values).

Code: `Source/Game/GameSandboxOpening.cs` (the server's beats, the wire, the save), `Source/Game/GameSpaceStart.cs` (a
ship start), `Source/App/Program.Opening.cs` (the camera, the screen and the skip key). Data: `Story/Opening/*.json`.

## When it runs

- **A new world** on a start whose `"opening"` names a file. Survival builds it before the start kit; Creative builds only
  a ship start's ship (no kit).
- **Every player, once per world**: the host, each friend and every late joiner, on first arriving alive. In a saved world
  "once" is the person's record (`WorldPlayer.Opened`); in an unsaved match, once per player id. A world from before starts
  (`StoryStarts.Legacy`) has no opening.
- **Together**: friends who arrive while someone is still on the card join their group and wake together, each to their
  own side. Anyone arriving later has a card of their own.
- **Skip**: the jump key, at any beat (for that person only).

## The beats

| Beat | What happens |
| --- | --- |
| Placeholder (`OpeningStage.Out`, `"placeholder"` in the file) | Black, lying where they wake, with the caption. |
| Wake (`"wake"`) | The black lifts; the lines (if any, signed by the script's `"warden"`, else plain); they get up over the last second. |

While it runs the player lies prone and nothing they press acts (`OpeningFilter` on both sides, `OpeningInput` on the
client), apart from the skip key; the start kit's notice waits until they wake. Killed during it, the opening ends.

## An opening file

Format `call-of-slop-opening/1`: `"warden"` (who says the lines),
`"wreck"` (a blueprint or part left 70 to 140 m out, `"crashFrom"`/`"crashTo"`), `"skip"` (the hint), `"ship"`,
`"orbit"` (m over the ground, at most 150 km), `"site"`, `"siteGap"` and `"siteIdle"` (a blueprint built on the ground with everyone waking in front of it: STARTS.md "A site start"), `"hold"` (true: the ship is built at rest and its flight computer holds it
there, not orbiting; a driver at the controls takes it back), `"wake"` and `"company"` (a ship start), and `"beats"` with `"placeholder"` and `"wake"` (each `"time"`,
`"caption"`, `"lines"`). A `"colony"` key is refused (the penal colony was removed). Text is ASCII; a bad file is refused
with a reason and there is no opening.

## Network and save

- **`Msg.Opening`** (protocol 199), server to one client, reliable: the beat, the time into it, its length, the camera's
  focus, the caption and the line just said. **`CActKind.OpeningSkip`** asks to skip.
- **The world save**'s `Opening` section (tag `OPEN`): the map, the start's ship (the field once held the colony), the
  crash site, and who has been through it. An older world's section goes on with the penal colony's barracks, depot, data
  centres, quarry and buildings: those are left unread, and the bodies stay as plain constructs.
