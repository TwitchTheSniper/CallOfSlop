# The story layer

The story layer turns what happens in a world into stories that can be seen, kept and, later, acted on. It is the
architecture from Karl's paper *Architecting Purpose in Space Simulations* (the five boxes), built in our own C#:

1. **Relational world state**: who and what exists, and how they stand to each other (relationship pairs).
2. **Incremental story sifter**: patterns of events matched as they happen (Winnow and Awash style partial matches).
3. **Drama manager**: watches the stories in progress and gives stage directions (phase 3: it nudges the people).
4. **NPC utility AI and HTN planner**: people who act on the world state with the same verbs as players (phase 2,
   headless: they live in the story world, without bodies yet).
5. **Presentation and milestones**: a library of plays, a legacy roster, contextual dialogue. *Phase 5.*

Phase 1 built boxes 1 and 2, the appraiser that feeds them from Sandbox play, the world save, and the Story window that
shows it all in 2D. Phase 2 adds box 4: people with needs, goals and plans who act on the world state and learn what
happens around them ("People" below). Phase 3 adds box 3: a drama manager that ranks the stories in progress and nudges
the person who would act next towards what the story waits for ("Drama" below). The plan, with the phases and the
reasons for each choice, is the project doc "Story systems plan".

## Trying it

- **The Story window**: **F4** in Sandbox (rebindable as *Story window*; also the *Story* button in the F7 toolbar). Host
  only for now: a joined player sees a note. Six tabs:
  - **World**: the story world as a graph. Players orange, people cream, places green, constructs blue; each
    relationship an arrow from the one who holds it, coloured by relation (two-way ones a plain line), thicker when
    stronger. Click an entity for what it holds, who holds what towards it, its stories and its last events; drag one to
    move it, drag the background to pan, the wheel zooms, Fit brings everything back into view. Filters: gone and
    scripted entities, each relation. The query box answers questions like `?a Grudge ?b >= 20, ?b Kills`.
  - **Stories**: where each story is at. Each pattern with its counts and its stages, and under it every match in
    progress as a row: the stages done (green, with the event that did it), the one waited for (orange, with the time
    left) and those still to come (grey, with the names bound so far). Then the stories told, newest first.
  - **People**: everyone with a mind of their own: where they are, what they are doing (with how far along) and the
    goal it is for. Pick one for their needs as bars, traits and feelings, every goal's score best first (hover one for
    each consideration's factor), the plan tree with the step under way, what they did lately (failures in red) and
    what they know, most interesting first, with who told them. *Add cast* puts a cast (Story/Casts) into the live
    world, its world place being this map; *Skip 10 min* moves the story clock on. A person under a stage direction
    shows it in blue (and in their goals' tooltips).
  - **Drama**: the drama manager. Its stage directions (who is nudged to do what to whom, for which story, how hard,
    how long it has left, and whether they did it), the stories in progress as it ranked them (hover one for its score
    taken apart), why the rest got nothing (a player acts next, the verb waits for bodies, nobody knows, nobody would),
    and what it did lately. *Nudge the people* turns it off and on; the slider sets its strength.
  - **Chronicle**: every event kept, newest first (filter by verb, or stories only), and what the sifter did.
  - **Data**: the scenarios (Play puts one into the live world), the patterns, verbs, relations and qualities loaded.
- **`--story-sim FILE`**: plays a scenario headless on its own clock and prints the chronicle, what the sifter did after
  each event, each pattern's counts, the world at the end, the scenario's expected counts, the sifting cost, and a save
  round trip. The exit code is the number of misses. `--story-sim` alone lists the scenarios. A scenario with a cast
  runs the people too (the clock a second at a time) and prints them at the end, with what an hour more costs;
  `--trace` (or `--trace=NAME`) prints each choice, plan, failure and piece of news learnt as it happens. The drama
  manager's directions print among the events, as they are given, followed or lapse. `--drama=off` runs without the drama manager;
  `--drama=compare` (with `--runs=N`, 8 by default) plays the scenario N times with seeds in turn, without and with it,
  and prints each pattern's stories both ways. `--strength=X` sets how hard it nudges (0.6 by default).
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

`StoryWorld` holds entities: a kind (Player, Person, Faction, Place, Construct, Thing: a resource, a part or a machine tier), a name, qualities (numbers such as
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
| Mine, Refine | a milestone: a player's machines have gathered or refined their first 1 t, 10 t or 100 t of one material (value: the tonnes) | `GameStoryWork.cs`, from `ConstructWorld.Yield` |
| Make | a milestone: the first part a player's assemblers make on each machine tier (value: the tier) | `GameStoryWork.cs`, `CNoteKind.Made` note |
| Story | built in: a pattern completed (its value is the pattern) | the engine |

Who did it: a player's shots and blasts set `ConstructWorld.DamageBy` around the damage (`SandboxShotDamage`,
`SandboxBlast` with `by`); a construct's guns are its owner's doing. A pilot is the driver, else the owner. The same
actor doing the same Strip, Tow or Board to the same target within 5 minutes is one event (stripping a wreck part by
part is one Strip).

## Milestones (mining and making)

Small steady work never becomes an event. Drills and saws (`ConstructGatherer`), buckets (`ConstructScoops`) and
refining machines (`ConstructRefining`) report what went into a bin with `ConstructWorld.Yield`, which sums it per
owner and material (no allocation, no note). Once a second the appraiser (`Game/GameStoryWork.cs`) adds the sums to
the **Mined** and **Refined** relations, from the construct's owner to the material as a `Thing` entity, in kilograms
(units times the material's `kgPerUnit` in Recycling.json), and records a Mine or Refine only when a total crosses
1 t, 10 t or 100 t. An Assembler's finished batch is a `Made` note: it counts in **Made** (player to part) and, the
first time a player makes a part on a machine of a tier (**MadeOn**, player to "tier N machines"), records a Make.
The machine code only reports; the amounts it deposits are unchanged.

A construct with no owner (the map's, a test's) is nobody's work. Every milestone adds to the **Milestones** quality.
`Story/Patterns/Work.json` has two stories built on them (From the ground up, Scrap into parts), and the Workshop
scenario plays both. A scenario event takes a `"value"` (the tonnes, the tier); sentences fill `{value}`.

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

## People (box 4)

People are `Person` entities with a mind of their own (`Source/Story/StoryAgents.cs`, the planner `StoryHtn.cs`, the
rules `StoryPeopleRules.cs`; data `Story/People/*.json` and `Story/Casts/*.json`). They live in the story world, with no
bodies yet (phase 4), and act through the same verbs as players, so the sifter reads their stories like anyone's.

- **Needs** (`"needs"`): qualities from 0 (desperate) to 100 (met) that fall by the hour (Energy, Company, Purpose,
  Supplies). Actions refill them. They show as qualities, so queries and the graph see them.
- **Actions** (`"actions"`): a verb a person can do, how long it `"takes"` (`"4m"`, or `"road"`: the Road's minutes),
  its condition (a query over `?me ?here ?target ?owner ?place ?item`), the needs it refills, and flags: `"chronicle":
  false` for routine ones (their effects happen, no event), `"news"` (Tell: it passes on news), `"sneaky"` (Steal: the
  one robbed doesn't see it). The condition is checked when planned, when the step starts and when it ends.
- **Tasks** (`"tasks"`, HTN): a compound task tries its methods in order; a method has a condition (what it finds can
  be passed on: `"?who In ?p"` then `{"task": "GoTo", "args": ["?p"]}`) and steps, each an action
  (`{"do": "Chat", "target": "?who"}`) or a task. The planner works depth first on the real world state inside a trial
  (every change logged and rolled back, `StoryWorld.BeginTrial`), so a later step sees what earlier ones change (after a
  Travel, `?me In ?here` is the new place). Bounded: 400 expansions, depth 12, 16 steps.
- **Goals** (`"goals"`, utility): a goal's score is its weight times each `"score"` line, each an input mapped from
  `from..to` onto 0..1, through a curve (linear, inverse, square, inverse square, root), and kept at least `min`. Inputs:
  `need`, `quality` (mine), `their` (the target's), `rel` (mine towards them), `held` (theirs towards me), `news` (the
  best news I have for them), `here` (they are where I am), `since` (how long since the goal last ended; `"from"` and
  `"since"` take times). `"if"` gates it; `"pick"` finds the candidates for `?them` and each is scored. Scores get a
  little jitter (8% each way) so people differ, the goal under way counts 1.5 times (people stick to what they
  started), and below 0.1 nothing is worth doing: they wait half a minute. The best goal that plans is taken; one that
  finds no plan rests five minutes for that target.
- **Thinking**: a person thinks only when their step ends or their wait runs out, and at most four think in one host
  tick. A step under way takes its time on the story clock; a trip takes them off the map (no `In`) until they arrive.
- **Knowledge**: what happens where someone is, they see (the actor always knows; a sneaky verb's target doesn't).
  Told news (a Tell whose value is the event told of, its item and owner who did what to whom) reaches the listener and
  everyone there. Each piece has an interest (`"news"`: per verb) plus what reactions add, halving every hour of play;
  a person keeps the 32 most interesting above `"keep"`. **Reactions** (`"reactions"`) are effects when someone learns
  of an event (`"if"` over `?me ?actor ?target ?owner ?place ?item`): hearing you were robbed gives a grudge, hearing of
  a kill makes you fear the killer. Every piece records how it was learnt (seen, told by whom, did it; radio later).
- **Casts** (`Story/Casts/*.json`): places (one may be `"world": true`, the host's world), roads with their minutes,
  people (home, work, qualities as traits from 0 to 100, Wealth, Trader) and starting feelings. A scenario names one
  with `"cast"` (and `"run"`: how long the clock goes on after its last event, `"seed"`: the people's random seed).

The shipped data: `Life.json` (needs, eleven actions, the tasks, twelve goals from Rest and Work to Get even and Keep
away), `News.json` (interest and reactions), the Frontier cast (twelve people in Ridge Camp, The Depot and Landing,
with a few grudges, friendships and a debt), `Patterns/People.json` (Word gets around, Thief found out, Thief
confronted, Debt repaid, Bad blood, Bit the hand that fed, Took them in) and `Scenarios/Frontier.json` (two players
passing through, four hours after). Relations can have a `"max"` (feelings stop at 100).

## Drama (box 3)

The drama manager (`Source/Story/StoryDrama.cs`) is Awash's Influence Mode: it doesn't write stories, it helps the ones
already under way finish. The sifter's live partial matches are its candidates; for each, the stage it waits for names
who would act (bound, or anyone: then up to two people who know of the story's last event) and whom to.

- **Ranking**: a story's score is its pattern's `"drama"` (1 by default, 0 leaves it alone) times its next stage's
  `"nudge"` (likewise) times how far along it is (0.5 at the start to 1 a stage from the end), how new (a pattern told
  often counts less: 1 / (1 + 0.25 per time told)), whether players are in it (1.5), how pressing (0.8 to 1.2 as its
  time runs out) and how ready the person is (their best goal that could take the verb, half again at a score of 0.5).
  A story waiting for a player, for a verb people can't take (Kill, until bodies), for someone nobody knows of, or for
  a person with no goal that would take it now, gets nothing: a nudge never makes someone act against their nature.
- **Stage directions**: the best eight, one a person, get one: the verb, the target, and for Tell the news it needs (who
  did what to whom). It lasts until the story's next stage would run out (an hour at a time for one with no limit). A
  direction already given ranks 1.25 times higher, so people don't flit between stories.
- **Following one**: the person's goals whose plans can take that verb (worked out from the HTN tasks at load) score
  `1 + bonus` times as much for that target, the bonus being up to the strength (0.6: x1.6) for the best ranked story
  and half of it for the least. News that fits a Tell direction is worth that much more to tell. Nothing else changes:
  their needs, feelings and plans decide as before.
- **When**: it ranks once a second at most, when an event came in or half a minute went by, and never per tick; with
  nobody to nudge it does nothing. Directions aren't saved: they are worked out again from the live matches on load.

Measured like Awash (whose study went from 45 to 52 completed patterns): `--story-sim Story/Scenarios/Frontier.json
--drama=compare --runs=12` tells 351 stories with the drama manager against 301 without (Bad blood 108 against 72,
Thief confronted 38 against 33), with about half of its directions followed by their person and a quarter done by
someone else first.

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
on average, including the stories it completes. The Frontier scenario's twelve people: a think takes about 25 us on
average (scoring every goal and planning the best); an hour of host ticks with them costs about 90 ms in all and
allocates nothing. A drama rank over the Frontier stories takes about 35 us. The save holds each person's needs, goal, step under way, goal times and knowledge (about 40 KB
with 400 events); a plan is made again after a load. The window costs one bool check closed; open, its graph layout is
O(n^2) over the entities shown (at most 400).

## Next

- **Phase 2** (done): people in the story world, headless. Next for them: news from radios, more casts, factions.
- **Phase 3** (done): the drama manager nudging the people. Next for it: casting someone new into a story (a stranger
  arrives), pacing (quiet stretches between big stories), and nudging bodies once they exist.
- **Phase 4**: NPCs with bodies (Sandbox spawned bots acting out their plans).
- **Phase 5**: presentation: milestones, the legacy roster, dialogue rules.
- Clients don't see the story yet: it would go to them in its own message.
