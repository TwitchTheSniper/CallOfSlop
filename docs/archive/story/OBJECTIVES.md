# The "what next" line

Karl, 2026-11-13: after the tutorial nothing on screen told a player their current goal. Now, once the warden's induction and
the first Ministry duty are over, a story player always has one short goal on the HUD, where the induction's panel sat (top
left), carrying them from inmate work towards the technology side: standing, the robot shells, a craft of their own, leaving
the planetoid, landing elsewhere, a spare body, and up the Ministry's ladder.

Code: `Source/Game/GameObjectives.cs` (the judging, the wire, the data), `Source/UI/UiObjectives.cs` (the HUD).
Data: `Story/Objectives.json`.

## The data

Format `call-of-slop-objectives/1`: `voice` (the tag a memo goes out under, "MINISTRY MEMO"), `hintAfter` (seconds) and
`goals`, in order. Each goal has `id`, `kind`, `title`, `text`, and optionally `memo` and `hint`; a `standing` goal also has
`faction` and `tier` (names as in Story/Factions). `{have}` and `{need}` in the text are a standing goal's numbers;
`{Action}` names show as the player's key. A bad file is refused (logged `[objectives] refused: ...`) and there is no line.

| Kind | Met when (the host sees it) |
| --- | --- |
| `standing` | the faction's standing with them (or their group) has reached the tier's lowest value |
| `depot` | their Orbital Depot run is done (docs/story/DEPOT.md); a world with no depot: met |
| `ship` | they flew a craft of their own (or their faction's) with thrusters, 15 m up from where they sat, not the depot's lander |
| `space` | they sat in a craft out past the sky's edge (or went to another planetoid) |
| `landing` | they stood on another planetoid (away on one the host runs, or the whole game landed elsewhere) |
| `shell` | they woke in a robot shell |

The current goal is the first one not met, so a goal met early (a ship built before the depot) is simply skipped. The firsts
(`ship`, `space`, `landing`, `shell`) are the person's milestones, saved with the world and celebrated when reached
(`Source/Game/GameMilestones.cs`, read by `ObjectiveFirst`).

## When it shows

Survival story worlds only, on the host's main game (not a planetoid's own). The host holds the goal back (sends none) while
the opening has them, the induction runs, or the quarry's or the depot's steps or report are up; the HUD also waits out those
panels' closing seconds, and nothing shows during a story ride (the crew bus scene replaces the HUD), in the Esc menu or the
menus. The memo shows for its first 20 s, the hint from `hintAfter` for a minute; otherwise just the title and one line. A
goal just met shows "Filed: ..." for 5 s. The words use Settings > Video "Story text" size and colour.

## Network and cost

`Msg.Objective` (server to one client, reliable ordered: u8 goal index, 255 none; two i16 as u16, have and need) only when it
changes (protocol 273). The host's own player hears it with no packet. Once a second the host walks the bodies once to see
what each owner has (the parts only while someone's goal is the shell), then judges each person: fixed arrays by player id,
nothing allocated. The HUD makes its words only when the goal, the numbers or a key change.
