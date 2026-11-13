# Story starts

Karl (2026-11-13): "allow it such that there may be different starts to the game on different planets/characters/backgrounds".
A start describes how a new story world begins. The Ministry of Slop's penal colony on Glacier is the first and default one;
other starts are data files beside it. Code: `Source/Game/StoryStarts.cs` (the files), `Source/Game/GameStoryStart.cs` (the
game's side). Check them with `--check-starts` (to `callofslop.log` next to the DLL).

## A start file

`Story/Starts/<Id>.json`, format `call-of-slop-start/1`. The file's name is its id, which the settings (`story_start`) and a
saved world keep. Text is ASCII.

| Key | What |
| --- | --- |
| `name`, `order` | the Story page's chooser (low order first) |
| `default` | the start a world without one plays: old saves, and survival worlds on any other map |
| `placeholder` | a rough start; the chooser says "(placeholder)" |
| `character`, `background`, `about` | who you are, a line on who you were, the Story page's paragraph |
| `map` | the planetoid it begins on (a Sandbox map) |
| `opening` | a file in `Story/Opening` (docs/story/OPENING.md), or `"none"`. An opening whose `"colony"` is `""` has no colony: everyone wakes at the start, with the wreck (when named) out past them and the cast (when named) in the story; the Ministry's colony, quarry, depot and data centres are not built |
| `tutorial` | `"Induction"` (the warden's induction, docs/sandbox/TUTORIAL.md) or `"none"`. Only the induction exists: another tutorial would have to reach clients, which load the steps themselves |
| `faction`, `standing` | a faction (Story/Factions, by name) and the standing each player starts on with it as they wake, when it isn't 0 and the faction has made nothing of them yet |
| `kit` | the start kit by blueprint or part name; left out: the shipped one (`Constructs/SurvivalStart.json`). A map's own `"survivalStart"` still wins |

## How a world picks its start

- **New world**: the host's choice (the Story page's Start row, or the Story start list under HOST STORY CO-OP), on that
  start's own planetoid. A start asked for on another map gives the default there, so every other survival world plays as
  it did before starts.
- **Saved world**: the start it was made with. The save keeps it in a `STRT` section, written only for a start that isn't
  the default, so a default world's save is unchanged and a world from before starts plays the default.
- **Server only**: clients never need the start. The opening's captions and lines come from the server, the tutorial is the
  induction or none (decided on the server), and the skip hint is the default opening's. No protocol change.

## The starts now

- `PenalColony` (default): Ministry convict on Glacier, the crash card, waking in the colony, the induction.
- `Castaway` (PLACEHOLDER, only proves starts work): Highland castaway on Caledon, a card, waking beside the wreck alone, no
  tutorial, Clan MacSlop standing 20 ("Friend of the Clan"). Replace or remove it when a real second start is written.
