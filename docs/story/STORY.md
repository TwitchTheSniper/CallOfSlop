# The story layer

The story layer turns what happens in a world into stories that can be seen, kept and, later, acted on. It is the
architecture from Karl's paper *Architecting Purpose in Space Simulations* (the five boxes), built in our own C#:

1. **Relational world state**: who and what exists, and how they stand to each other (relationship pairs).
2. **Incremental story sifter**: patterns of events matched as they happen (Winnow and Awash style partial matches).
3. **Drama manager**: watches the stories in progress and gives stage directions. *Phase 3.*
4. **NPC utility AI and HTN planner**: people who act on the world state with the same verbs as players. *Phase 2.*
5. **Presentation and milestones**: a library of plays, a legacy roster, contextual dialogue. *Phase 5.*

Phase 1 (this) builds boxes 1 and 2, the appraiser that feeds them from Sandbox play, the world save, and the Story
window that shows it all in 2D. The plan, with the phases and the reasons for each choice, is the project doc
"Story systems plan".

## Trying it

- **The Story window**: **F4** in Sandbox (rebindable as *Story window*; also the *Story* button in the F7 toolbar). Host
  only for now: a joined player sees a note. Four tabs:
  - **World**: the story world as a graph. Players orange, people cream, places green, constructs blue; each
    relationship an arrow from the one who holds it, coloured by relation (two-way ones a plain line), thicker when
    stronger. Click an entity for what it holds, who holds what towards it, its stories and its last events; drag one to
    move it, drag the background to pan, the wheel zooms, Fit brings everything back into view. Filters: gone and
    scripted entities, each relation. The query box answers questions like `?a Grudge ?b >= 20, ?b Kills`.
  - **Stories**: where each story is at. Each pattern with its counts and its stages, and under it every match in
    progress as a row: the stages done (green, with the event that did it), the one waited for (orange, with the time
    left) and those still to come (grey, with the names bound so far). Then the stories told, newest first.
  - **Chronicle**: every event kept, newest first (filter by verb, or stories only), and what the sifter did.
  - **Data**: the scenarios (Play puts one into the live world), the patterns, verbs, relations and qualities loaded.
- **`--story-sim FILE`**: plays a scenario headless on its own clock and prints the chronicle, what the sifter did after
  each event, each pattern's counts, the world at the end, the scenario's expected counts, the sifting cost, and a save
  round trip. The exit code is the number of misses. `--story-sim` alone lists the scenarios.
- **`--check-story [DIR]`**: loads the story data and every scenario and says what is refused and why.

## How it flows

```
game moment ──> appraiser (Game/GameStory.cs) ──> StoryEngine.Record(verb, actor, target, owner, place, item)
                                                     ├─ the verb's effects change the world state
                                                     ├─ the event goes into the chronicle
                                                     └─ the sifter: matches end, advance, start
                                                          └─ a completed match: its "then" effects, and a Story event
                                                             recorded in turn (patterns can build on stories)
```

Nothing runs per tick except the story clock and, once a second, the sifter's time limits. Everything happens when an
event arrives. Code: `Source/Story/` (the engine, no game types), `Source/Game/GameStory.cs` (the appraiser),
`Source/Constructs/ConstructWorldNotes.cs` (what constructs tell it), `Source/UI/UiStory.cs` and
`Source/App/Program.Story.cs` (the window). Data: `Story/` next to the game.

## The world state (box 1)

`StoryWorld` holds entities: a kind (Player, Person, Faction, Place, Construct), a name, qualities (numbers such as
Kills) and relationship pairs. A pair is (relation, target) packed in one `ulong` with a strength, kept sorted on the
subject and indexed the other way on the target (`Held`), so "whom does Karl hold a grudge against" and "who holds one
against Karl" are both a binary search. A relation can be exclusive (one target at a time: `In`) or symmetric (both
ways at once: `Knows`). Story worlds hold hundreds to low thousands of entities, so sorted arrays per entity are all it
needs: no archetype tables. Changes happen only on events.

An entity is found again by its game object's key: a person by their world key (Settings.WorldKey, sent in the Hello),
or their Steam id; a place by its name; a construct by its id. A construct joins the story the first time something
happens to it (or when spawned from a blueprint), named after its owner and blueprint ("Karl's Buggy (21)") or what it
is ("Karl's vehicle (12)"), with its owner's `Owns`. Wreck pieces split off a construct point at its entity, so
stripping a piece of Karl's buggy is stripping Karl's buggy. When no game object points at an entity any more it is
*gone*: it stays, with its history, and the window hides it unless asked.

**Queries** (`StoryQuery`): clauses joined by commas, solved by backtracking that generates from the pair indices both
ways and allocates nothing unless results are collected.

| Clause | Means |
| --- | --- |
| `?a Grudge ?b`, `?a Grudge ?b >= 20` | a relationship is there, or its strength compared |
| `?a Kills`, `?a Kills > 2` | a quality is not zero, or compared |
| `?a is Player` | an entity's kind |
| `?a != ?b`, `?a == ?b` | two terms compared |
| `not ...` before any of the first three | there is no such thing |

A term is a variable (`?a`) or an entity by name (`Karl`, `'Glacier Station'`).

`Story/Relations.json` lists the relations and qualities.

## The actions (verbs)

`Story/Verbs.json`: one list for players and NPCs, so a player's real action and an NPC's planned one (phase 2) leave
the same trace. Each verb has roles (actor, target, owner, place, item), a sentence and effects:
`"subject Relation object +n|-n|=n"` (`=0` removes; no amount sets 1) or `"subject Quality +n"`. An effect on an empty
role, or from someone to themselves, does nothing. **Physical verbs never resolve abstractly**: a kill is recorded when
the game kills, never decided by the story (no X4-style out-of-sight simulation that can disagree with what is seen).

| Verb | Recorded when (host, Sandbox) | Where |
| --- | --- | --- |
| Join, Leave | a person comes into the world (once their Hello has said who they are), leaves | `StoryTick`, `SandboxModule.OnPlayerLeave` |
| Found | a construct spawned from a blueprint (parts menu) | `SandboxSpawnBlueprint` |
| Board | sitting in someone else's construct, or one's own the first time | `StoryTick` (the hands' seat) |
| Kill, Die | a kill; a death with nobody to blame (one's own grenade, a fall) | `SandboxModule.OnKill` |
| Wreck | a construct first disabled (a part shot or blown off, a route or track cut) by a known attacker or of a player's construct | `ConstructWorld.Disable` note |
| Strip | taking a part off someone else's wreck or disabled construct | `ConstructEdits` remove, note |
| Tow | coupling a hitch to someone else's construct | `ConstructEdits.Hitch`, note |
| Launch, Land | a construct leaves a world's sky as a ship, comes back down | `GameSpaceLaunch.cs` |
| Jump, Discover | a jump drive jump; the first arrival anyone made in that system | `GameSpaceJump.cs` |
| Story | built in: a pattern completed (its value is the pattern) | the engine |

Who did it: a player's shots and blasts set `ConstructWorld.DamageBy` around the damage (`SandboxShotDamage`,
`SandboxBlast` with `by`); a construct's guns are its owner's doing. A pilot is the driver, else the owner. The same
actor doing the same Strip, Tow or Board to the same target within 5 minutes is one event (stripping a wreck part by
part is one Strip).

## Patterns and the sifter (box 2)

`Story/Patterns/*.json` (any number of files). An example: the shipped Score settled, with an `unless` and a `then`
added to show them:

```json
{
  "name": "Score settled",
  "about": "killed by someone, you kill them back",
  "tells": "{b} settled the score with {a}",
  "stages": [
    { "verb": "Kill", "actor": "?a", "target": "?b" },
    { "verb": "Kill", "actor": "?b", "target": "?a", "within": "2h" }
  ],
  "unless": [ { "verb": "Leave", "actor": "?a" } ],
  "then": [ "b Grudge a =0" ],
  "story": { "actor": "?b", "target": "?a" }
}
```

- **stages** happen in order, with anything else in between. A variable binds on the first stage that names it and must
  match after. A role a stage binds must be filled. `"pattern": "Name"` on a `Story` stage matches only that pattern's
  stories.
- **within**: the time allowed since the stage before (`"90s"`, `"5m"`, `"2h"`, `"1h30m"`).
- **where**: a query on the world state at that moment, over the pattern's variables.
- **unless**: an event that ends the match (a variable not yet bound there means anyone).
- **then**: effects when it completes, on the pattern's variables (`a`, `b`, ...).
- **tells**: its sentence, with `{var}` filled. **story**: whom the Story event names.

The sifter keeps a pool of partial matches (512, at most 64 per pattern; when full, the oldest, least advanced goes).
Each event first ends the matches an `unless` names, retires those whose time ran out, advances those waiting for its
verb in place, then starts new ones. The same story begun again (A kills B twice) refreshes its match instead of
doubling it. A completed match applies its `then` and records a Story event, which later patterns can match (four
deep at most). Events that are part of a match or story are kept; past 20,000 events the oldest plain ones fold into a
count per verb.

The six patterns in `Friends.json` come from what players already do: Score settled, Tit for tat, Vultures, Good turn
returned, Bit the hand, Round trip.

## Scenarios

`Story/Scenarios/*.json`: entities to make (`"owner"` gives a construct its owner's `Owns`), events at times from the
start, and the count of each pattern it should complete (`"expect"`). An event's empty owner is the target's owner, as
the game fills it. `--story-sim` plays one on its own clock (time limits run out on the way); the window plays one into
the live world, an event every 0.75 s, marked scripted, with entities of its own, so a scripted "Karl" never touches
the real one.

## Saving

The story is saved with the world, in a `Story` section of the world file (`WorldSaveWriter.Story`): on the tick a
plain copy (`StoryEngine.Capture` into a `StorySave` kept in the snapshot, 0.2 ms for a 30,000-event story), the bytes
on the save's worker. Relations, qualities, verbs and patterns are written by name and mapped back on load, so a save
outlives data changes: what no longer exists is left out. A story that doesn't read is left out and starts afresh; the
world still loads. Without a world (plain Sandbox), the story lives for the session.

## Costs

`--story-sim` on 30,000 random events among 60 players and 120 constructs (every pattern's pool full): 11 us an event
on average, including the stories it completes. The window costs one bool check closed; open, its graph layout is
O(n^2) over the entities shown (at most 400).

## Next

- **Phase 2**: NPCs in the story world (people with needs, utility scores and HTN plans over the same verbs), headless.
- **Phase 3**: the drama manager reading the stories in progress and nudging them.
- **Phase 4**: NPCs with bodies (Sandbox spawned bots acting out their plans).
- **Phase 5**: presentation: milestones, the legacy roster, dialogue rules.
- Clients don't see the story yet: it would go to them in its own message.
