# Starts

Karl (2026-11-13): "allow it such that there may be different starts to the game on different planets/characters/backgrounds";
(2026-11-14) Survival and Creative, "both with the rust bucket as the first start you can choose from". A start describes
how a new world begins. The menus (Single player and Multiplayer) list the starts in order for both Survival and Creative.
The penal colony start and its tutorial, the warden's induction, were removed (2026-11-17; docs/removals-notes.md, the old
docs in docs/archive/colony).
The story itself is archived (docs/sandbox/SURVIVAL.md): starts stay, the story engine doesn't run. Code: `Source/Game/StoryStarts.cs` (the files), `Source/Game/GameStoryStart.cs` (the
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
| `opening` | a file in `Story/Opening` (docs/story/OPENING.md), or `"none"`. An opening with no `"ship"` wakes everyone at the start, with the wreck (when named) out past them and the cast (when named) in the story |
| `tutorial` | `"none"` or left out (the induction went with the penal colony; any other value is refused) |
| `faction`, `standing` | a faction (Story/Factions, by name) and the standing each player starts on with it as they wake, when it isn't 0 and the faction has made nothing of them yet (needs the story layer: nothing while it is archived) |
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
- `Magpie`: a prospector waking in a cabin aboard the Magpie Mining Ship (docs/sandbox/constructs/miner.md) 1.8 km over
  Crumb, Mille's 600 m moonlet in Frostreach (gravity 1.5, iron, stone and sand: docs/planets/WORLDS.md), so the first job is
  to set down and drill. No faction, the kit aboard. Survival and Creative.
- `ZeroDay`: a captain waking in a cabin aboard their own Zero Day Frigate (docs/sandbox/constructs/torpedo-frigate.md)
  over Crumb, where the Magpie start wakes, its magazines full and its banks charged (as every ship the game spawns). A
  Traders' Zero Day, crewed, goes round 400 m behind on the same orbit (Karl, 2026-11-17: an NPC Zero Day of another faction,
  neutral and nearby; the Traders are neutral to players and fly the IT style). Survival and Creative.
- `Ground`: the map picked on the menu, the shipped start kit round its parts rack in Survival, no opening: what every
  Sandbox world was before the openings.
- `PenalColony` was removed with the colony and its induction (2026-11-17): a world made with it plays the any-map start.
- `Castaway` (PLACEHOLDER) is archived with the story: docs/archive/story/starts.
