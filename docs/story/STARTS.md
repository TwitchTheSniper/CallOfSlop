# Starts

Karl (2026-11-13): "allow it such that there may be different starts to the game on different planets/characters/backgrounds";
(2026-11-14) Survival and Creative, "both with the rust bucket as the first start you can choose from", and the penal colony
"as a tickbox tutorial for survival", "its own world". A start describes how a new world begins. The menus (Single player
and Multiplayer) list the starts in order for both Survival and Creative; the start the warden's induction follows (the penal
colony) is not in that list but behind Survival's Tutorial tickbox, which plays it in a world of its own that is never saved.
The story itself is archived (docs/sandbox/SURVIVAL.md): starts stay, the story engine doesn't run. Code: `Source/Game/StoryStarts.cs` (the files), `Source/Game/GameStoryStart.cs` (the
game's side). Check them with `--check-starts` (to `callofslop.log` next to the DLL).

## A start file

`Story/Starts/<Id>.json`, format `call-of-slop-start/1`. The file's name is its id, which the settings (`story_start`) and a
saved world keep. Text is ASCII.

| Key | What |
| --- | --- |
| `name`, `order` | the menus' Start row (low order first) |
| `default` | the start a world saved without one plays (old saves: every one is a penal colony world) |
| `placeholder` | a rough start; the chooser says "(placeholder)" |
| `character`, `background`, `about` | who you are, a line on who you were, the menus' paragraph |
| `map` | the planetoid it begins on (a Sandbox map), or `"any"`: the map picked on the menu |
| `opening` | a file in `Story/Opening` (docs/story/OPENING.md), or `"none"`. An opening whose `"colony"` is `""` has no colony: everyone wakes at the start, with the wreck (when named) out past them and the cast (when named) in the story; the Ministry's colony, quarry, depot and data centres are not built |
| `tutorial` | `"Induction"` (the warden's induction, docs/sandbox/TUTORIAL.md) or `"none"`. Only the induction exists: another tutorial would have to reach clients, which load the steps themselves. The first start with the induction is the Survival tutorial's (`StoryStarts.Tutorial`) and the menus leave it out of the Start row |
| `faction`, `standing` | a faction (Story/Factions, by name) and the standing each player starts on with it as they wake, when it isn't 0 and the faction has made nothing of them yet (needs the story layer: nothing while it is archived) |
| `kit` | the start kit by blueprint or part name; left out: the shipped one (`Constructs/SurvivalStart.json`). A map's own `"survivalStart"` still wins |

## A ship start

An opening (Story/Opening) whose `"ship"` names a blueprint (and whose `"colony"` is `""`) builds it in orbit over the start
(`"orbit"`: m over the ground, at least the sky's edge plus 400 m), going round at a circular orbit's speed, and everyone
wakes standing in its frame at the `"wake"` spots (`[x, y, z]` in the ship's own metres from its first part's middle, one
each in turn). The next space tick makes it a ship (docs/space/SPACE.md). An opening's `"company"`
(`[{ "ship": BLUEPRINT, "faction": KIND, "behind": M }]`) adds a nobody's ship on the same circular orbit, `behind` m back
along it (negative: ahead; 150 m to 5 km), crewed by that NPC faction kind (Crew/Factions): `GameSpaceStart.OpeningCompanion`. The save keeps the ship where a colony start keeps
the colony, so friends who join later wake aboard it too. A ship start works in Creative too (no kit; the parts menu has
everything; stock guns kept): `SandboxStartKit` builds only the ship there. Code: `Source/Game/GameSpaceStart.cs`.

## How a world picks its start

- **New world**: the host's choice (the Start row on the Single player or Multiplayer page; `Settings.PlayedStartId` gives
  the tutorial's with its tickbox), on that start's own planetoid, or the map picked for an any-map start. A start asked for
  on another map (a host who changes the map in the lobby) gives the any-map start there (`StoryStarts.For`). Window-tool
  runs with no `--start` keep the old ways: `--survival` plays the penal colony, Creative the map as it is.
- **Saved world**: the start it was made with. The save keeps it in a `STRT` section, written only for a start that isn't
  the default, so a default world's save is unchanged and a world from before starts plays the default.
- **Server only**: clients never need the start. The opening's captions and lines come from the server, the tutorial is the
  induction or none (decided on the server), and the skip hint is the default opening's. No protocol change.

## The starts now

- `RustBucket` (first): a tramp hauler waking on the floor of their cabin aboard the Rust Bucket (docs/sandbox/constructs/ship.md)
  in orbit 3 km over Glacier; no colony, no tutorial, no faction, the kit aboard (`"kit": []`). Survival and Creative.
- `Magpie`: a prospector waking in a cabin aboard the Magpie Mining Ship (docs/sandbox/constructs/miner.md) 3.2 km over
  Caledon, Frostreach's 6 km highland world (gravity 11, stone, iron and sand: docs/planets/WORLDS.md; moved from Crumb, Karl
  2026-11-17: a bigger world with more gravity), so the first job is to set down and drill. No colony, no tutorial, no faction, the kit aboard. Survival and Creative.
- `ZeroDay`: a captain waking in a cabin aboard their own Zero Day Frigate (docs/sandbox/constructs/torpedo-frigate.md)
  over Caledon, where the Magpie start wakes, its magazines full and its banks charged (as every ship the game spawns). A
  Traders' Zero Day, crewed, goes round 400 m behind on the same orbit (Karl, 2026-11-17: an NPC Zero Day of another faction,
  neutral and nearby; the Traders are neutral to players and fly the IT style). Survival and Creative.
- `Ground`: the map picked on the menu, the shipped start kit round its parts rack in Survival, no opening: what every
  Sandbox world was before the openings.
- `PenalColony` (default, the Survival tutorial): Ministry convict on Glacier, the crash card, waking in the colony, the
  induction. With the story archived the colony has its buildings, quarry, depot, spaceport and labour gangs but no staff
  walking it (they are story people) and no data centres; the induction's lines still come, signed by the warden.
- `Castaway` (PLACEHOLDER) is archived with the story: docs/archive/story/starts.
