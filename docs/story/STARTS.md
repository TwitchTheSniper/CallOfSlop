# Starts

Karl (2026-11-13): "allow it such that there may be different starts to the game on different planets/characters/backgrounds";
(2026-11-14) Survival and Creative, "both with the rust bucket as the first start you can choose from". A start describes
how a new world begins. The menus (Single player and Multiplayer) list the starts in order for both Survival and Creative.
The penal colony start and its tutorial, the warden's induction, were removed (2026-11-17; docs/removals-notes.md, the old
docs in docs/archive/colony).
The story was removed (2026-11-21, docs/removals-notes.md): starts stay. Code: `Source/Game/StoryStarts.cs` (the files), `Source/Game/GameStoryStart.cs` (the
game's side). Check them with `--check-starts` (to `callofslop.log` next to the DLL).

## A start file

`Story/Starts/<Id>.json`, format `call-of-slop-start/1`. The file's name is its id, which the settings (`story_start`) and a
saved world keep. Text is ASCII.

| Key | What |
| --- | --- |
| `name`, `order` | the menus' Start row (low order first) |
| `default` | the start the menus fall back to (the rust bucket). A world saved without a start plays none of these: `StoryStarts.Legacy`, no opening |
| `placeholder` | a rough start; the chooser says "(placeholder)" |
| `character`, `background`, `about` | who you are, a line on who you were, the menus' paragraph |
| `map` | the planetoid it begins on (a Sandbox map), or `"any"`: the map picked on the menu |
| `opening` | a file in `Story/Opening` (docs/story/OPENING.md), or `"none"`. An opening with no `"ship"` wakes everyone at the start, with the wreck (when named) out past them |
| `tutorial` | `"none"` or left out (the induction went with the penal colony; any other value is refused) |
| `kit` | the start kit by blueprint or part name; left out: the shipped one (`Constructs/SurvivalStart.json`). A map's own `"survivalStart"` still wins |

## A ship start

An opening (Story/Opening) whose `"ship"` names a blueprint builds it in orbit over the start
(`"orbit"`: m over the ground, at least the sky's edge plus 400 m), going round at a circular orbit's speed, and everyone
wakes standing in its frame at the `"wake"` spots (`[x, y, z]` in the ship's own metres from its first part's middle, one
each in turn). The next space tick makes it a ship (docs/space/SPACE.md). An opening's `"company"`
(`[{ "ship": BLUEPRINT, "faction": KIND, "behind": M }]`) adds a nobody's ship on the same circular orbit, `behind` m back
along it (negative: ahead; 150 m to 5 km), crewed by that NPC faction kind (Crew/Factions): `GameSpaceStart.OpeningCompanion`. The save keeps the ship (the Opening section),
so friends who join later wake aboard it too. A ship start works in Creative too (no kit; the parts menu has
everything; stock guns kept): `SandboxStartKit` builds only the ship there. Code: `Source/Game/GameSpaceStart.cs`.

## A site start

An opening whose `"site"` names a blueprint (Sean, 2026-11-25: the Zero Day works test start) builds it whole on the ground,
on the flattest dry ground within 1.5 km of the map's spawn (its footprint's ground varying under 2.5 m is taken at once, else
the least varied; never a sea's water), its floor on the highest ground under it, turned as the blueprint is. Everyone wakes
`"siteGap"` m (default 15) out past its front, the blueprint's low-z end, facing it. `"siteIdle": true` builds every Line Console
on it stopped (its "start on" setting off too, so a save keeps it so). Survival and Creative: in Creative its stores come
as any creative spawn of the blueprint (the works' bunkers, silos and fuel full), in Survival empty, and `"kit": []` gives no
kit. The save keeps the wake spot (the Opening section's crash site). A site and a ship are not both allowed. Code:
`Source/Game/GameSiteStart.cs`.

## How a world picks its start

- **New world**: the host's choice (the Start row on the Single player or Multiplayer page; `Settings.PlayedStartId`), on
  that start's own planetoid, or the map picked for an any-map start. A start asked for
  on another map (a host who changes the map in the lobby) gives the any-map start there (`StoryStarts.For`). Window-tool
  runs with no `--start` play the any-map start, on the map as it is.
- **Saved world**: the start it was made with. The save keeps it in a `STRT` section, written for every start since
  2026-11-17 (before, the default's was left out). A world with none is from before starts, which made it a penal colony
  world: it plays `StoryStarts.Legacy`, with no opening, and its colony stays as plain constructs.
- **Server only**: clients never need the start. The opening's captions and lines come from the server, and the skip hint is the default
  opening's.

## The starts now

- `RustBucket` (first): a tramp hauler waking on the floor of their cabin aboard the Rust Bucket (docs/sandbox/constructs/ship.md)
  in orbit 3 km over Glacier; no faction, the kit aboard (`"kit": []`); the default. Survival and Creative.
- `Magpie`: a prospector waking in a cabin aboard the Magpie Mining Ship (docs/sandbox/constructs/miner.md) 3.2 km over
  Caledon, Frostreach's 6 km highland world (gravity 11, stone, iron and sand: docs/planets/WORLDS.md; moved from Crumb, Karl
  2026-11-17: a bigger world with more gravity), so the first job is to set down and drill. No faction, the kit aboard. Survival and Creative.
- `ZeroDay`: a captain waking in a cabin aboard their own Zero Day Frigate (docs/sandbox/constructs/torpedo-frigate.md)
  over Caledon, where the Magpie start wakes, its magazines full and its banks charged (as every ship the game spawns). A
  Traders' Zero Day, crewed, goes round 400 m behind on the same orbit (Karl, 2026-11-17: an NPC Zero Day of another faction,
  neutral and nearby; the Traders are neutral to players). Survival and Creative.
- `Interactions` (Karl, 2026-11-20: the trade world, the Traders first): the Magpie start's ship and orbit over Caledon with
  Caledon Highport 40 km up, its own card (`Story/Opening/Interactions.json`). The Traders run their supply lines between
  their stations (`Crew/Stations.json`), each station uses and makes goods, and trade is barter with a tab (`Game/GameTabs.cs`).
  The ship is a placeholder: the opening's `"ship"` (and its `"wake"` points) take any blueprint. Survival and Creative.
- `CapitalDuel` (two Mainframes held 10 km apart off Redrock) was removed with the Mainframe (Karl, 2026-11-20: capital ships
  on the back burner): docs/archive/capital/mainframe.md says how to bring both back. The openings' `"hold"` stays.
- `ZeroDayWorks` (TEST START, last; Sean, 2026-11-25: "a starting point that spawns you in front of an idle, full Zero Day
  factory, for quicker testing"): the whole Zero Day works (docs/sandbox/constructs/zeroday-works.md) on Caledon's flattened
  pole (the landing ground, 175 m across: WORLDS.md "Places kept flat"), its Line Consoles stopped, everyone waking 20 m south
  of its assembly yard, looking north into it. A site start (above); no kit. Survival and Creative.
- `ZeroDayWorks2` (TEST START, after it; Sean, 2026-11-25): Zero Day Works II, Sean's Factory Lab layout (`zeroday_works2`,
  Source/PartKit/Generators/ZeroDayWorks2.cs, the layout in Constructs/Factories/Layouts/ZeroDayWorks2.json) with no walls or roof,
  224 x 216 m (its corners 156 m from its middle, inside the pole's 175 m), Line Consoles stopped, everyone waking 20 m past its
  low-z end. A site start; no kit. Survival and Creative. Its four 714 kW gensets are bodies of their own beside it (a body runs one
  engine), each on a heavy power cable to a Heavy Power Terminal on the floor: power-and-links.md "Heavy power cables".
- `Ground`: the map picked on the menu, the shipped start kit round its parts rack in Survival, no opening: what every
  Sandbox world was before the openings.
- `PenalColony` was removed with the colony and its induction (2026-11-17): a world made with it plays the any-map start.
- `Castaway` (PLACEHOLDER) went with the story (docs/removals-notes.md).
