# The story layer

The story layer turns what happens in a world into stories that can be seen, kept and, later, acted on. It is the
architecture from Karl's paper *Architecting Purpose in Space Simulations* (the five boxes), built in our own C#:

1. **Relational world state**: who and what exists, and how they stand to each other (relationship pairs).
2. **Incremental story sifter**: patterns of events matched as they happen (Winnow and Awash style partial matches).
3. **Drama manager**: watches the stories in progress and gives stage directions (phase 3: it nudges the people).
4. **NPC utility AI and HTN planner**: people who act on the world state with the same verbs as players (phase 2:
   they live in the story world; phase 4: those in the host's world walk about it in bodies).
5. **Presentation and milestones**: contextual dialogue and the radio (phase 5: the people speak), a library of plays,
   a legacy roster.

Phase 1 built boxes 1 and 2, the appraiser that feeds them from Sandbox play, the world save, and the Story window that
shows it all in 2D. Phase 2 adds box 4: people with needs, goals and plans who act on the world state and learn what
happens around them ("People" below). Phase 3 adds box 3: a drama manager that ranks the stories in progress and nudges
the person who would act next towards what the story waits for ("Drama" below). Phase 4 gives the people in the host's
world bodies that act out their steps, and makes a killed person dead for good ("Bodies" below). Phase 5 is box 5:
the people speak by response rules, and a radio puts the news out to everyone ("Lines" below); short plays are cast
from the world and acted out ("Plays"), places change with what happened in them ("Places"), and a roster remembers
everyone the story has known ("Roster"). The plan, with the phases and the reasons for each choice, is the project doc
"Story systems plan".

## Trying it

- **The Story window**: **F4** in Sandbox (rebindable as *Story window*; also the *Story* button in the F7 toolbar). Host
  only for now: a joined player sees a note. Eight tabs:
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
    what they know, most interesting first, with who told them, and what they said lately (hover a line for the rule
    that said it). *Add cast* puts a cast (Story/Casts) into the live
    world, its world place being this map; *Skip 10 min* moves the story clock on; *Bodies* turns the people's bodies
    in this world on and off; *Talk* turns their words off and on. A person with a body is green (their page says where it is and where it is going), the
    dead are grey. A person under a stage direction shows it in blue (and in their goals' tooltips).
  - **Drama**: the drama manager. Its stage directions (who is nudged to do what to whom, for which story, how hard,
    how long it has left, and whether they did it), the stories in progress as it ranked them (hover one for its score
    taken apart), why the rest got nothing (a player acts next, the verb waits for bodies, nobody knows, nobody would),
    and what it did lately. *Nudge the people* turns it off and on; the slider sets its strength. *Pacing* turns the
    quiet stretches and strangers off and on, and the line beside it shows the stretch under way and the tension.
  - **Scenes**: the plays under way (who plays whom, the beat they are at, whether a beat waits for two to meet; hover
    one for its beats), the plays lately (played out or broken off, and why), *Try one now* (starts a play at once if
    it can be cast) and the places: each one's state, since when, and who is there. *Plays* turns them off and on,
    *Stop them* breaks off those under way.
  - **Roster**: everyone the story has known, people and players, the living and the dead, by renown: how they are now
    (where, or who killed them where and when), what they did, the stories they are in, and their line. Hover a name for
    all their deeds and stories; click one for their page on the People tab.
  - **Chronicle**: every event kept, newest first (filter by verb, or stories only), and what the sifter did.
  - **Data**: the scenarios (Play puts one into the live world), the patterns, the line rules by occasion (hover one
    for its lines), the verbs, relations and qualities loaded.
- **`--story-sim FILE`**: plays a scenario headless on its own clock and prints the chronicle, what the sifter did after
  each event, each pattern's counts, the world at the end, the scenario's expected counts, the sifting cost, and a save
  round trip. The exit code is the number of misses. `--story-sim` alone lists the scenarios. A scenario with a cast
  runs the people too (the clock a second at a time) and prints them at the end, with what an hour more costs;
  `--trace` (or `--trace=NAME`) prints each choice, plan, failure and piece of news learnt as it happens; `--lines`
  prints what they say (each step's line as it starts, as headless there are no bodies to meet), with the plays as
  they begin and end; `--roster` prints the roster at the end. A summary gives the places' states and the plays. The drama
  manager's directions print among the events, as they are given, followed or lapse. `--drama=off` runs without the drama manager;
  `--drama=compare` (with `--runs=N`, 8 by default) plays the scenario N times with seeds in turn, without and with it,
  and prints each pattern's stories both ways. `--strength=X` sets how hard it nudges (0.6 by default); `--pacing=off`
  nudges at full strength all the time and brings no strangers ("Pacing" below).
- **`--check-story [DIR]`**: loads the story data and every scenario and says what is refused and why, and lists the
  place states and the plays.

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
| Attack, Fight, Flee, Defend | someone hurts a person's body; the person (or a friend who hears them) fights back, runs, or stands up for them ("Fights") | `GameStoryFights.cs` |
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
rules `StoryPeopleRules.cs`; data `Story/People/*.json` and `Story/Casts/*.json`). They live in the story world, those in
the host's world with bodies there ("Bodies" below), and act through the same verbs as players, so the sifter reads
their stories like anyone's.

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
- **Death**: a person who is a Kill's target or a Die's actor dies for good (Karl, 2026-11-04). What they were doing
  stops, they leave their place for none, they carry the `Dead` quality (that is what the save keeps, and queries can
  read it), they learn nothing more and nobody picks them for a goal again; every story still waiting for them to act
  or be acted on ends there (the sifter's Ended, by their death). Witnesses fear the killer (News.json) and keep away
  from them; word of it gets around. Players never die for good: they come back.
- **Knowledge**: what happens where someone is, they see (the actor always knows; a sneaky verb's target doesn't).
  Told news (a Tell whose value is the event told of, its item and owner who did what to whom) reaches the listener and
  everyone there; news on the radio (a Broadcast, likewise) reaches everyone in a place (the road has no radio). Each piece has an interest (`"news"`: per verb) plus what reactions add, halving every hour of play;
  a person keeps the 32 most interesting above `"keep"`. **Reactions** (`"reactions"`) are effects when someone learns
  of an event (`"if"` over `?me ?actor ?target ?owner ?place ?item`): hearing you were robbed gives a grudge, hearing of
  a kill makes you fear the killer. Every piece records how it was learnt (seen, told by whom, did it, on the radio from whom).
- **Casts** (`Story/Casts/*.json`): places (one may be `"world": true`, the host's world), roads with their minutes,
  people (home, work, qualities as traits from 0 to 100, Wealth, Trader), starting feelings, and strangers who may come
  later ("Pacing" below). A scenario names one
  with `"cast"` (and `"run"`: how long the clock goes on after its last event, `"seed"`: the people's random seed).

The shipped data: `Life.json` (needs, eleven actions, the tasks, twelve goals from Rest and Work to Get even and Keep
away), `News.json` (interest and reactions), the Frontier cast (twelve people in Ridge Camp, The Depot and Landing,
with a few grudges, friendships and a debt), `Patterns/People.json` (Word gets around, Thief found out, Thief
confronted, Debt repaid, Bad blood, Bit the hand that fed, Took them in, Fled the killing: someone who now fears a
killer leaves the place soon after; and the fights': Stood their ground, Came to their aid, Turned the tables) and `Scenarios/Frontier.json` (two players passing through, four hours after);
`Scenarios/Killing.json` has a player kill Rhea Lund at the landing. `Scenarios/Fight.json` has a player shoot Hugo Brandt there: he
fights back, Sol Reyes comes to his aid, Ada Quill runs, and Hugo kills the player ("Fights"). A scenario with a cast may name the cast's people in
its events. Relations can have a `"max"` (feelings stop at 100).

## Bodies (phase 4)

On the host in Sandbox, each person the story has in this world (`In` the map's place, which a cast's `"world"` place
is) walks about it as a bot of the people's own side (`Source/Game/GameStoryBodies.cs`, team 12), named after them:

- **Coming and going**: a person here gets a body at the spot of what they are doing. A trip here shows its body at that
  road's end (40 to 50 m out, the same spot for the same place) 20 s before it is over, and it walks in; a person setting
  off walks out to the road's end and goes. At most 8 bodies, one player id always kept free for a person joining. They
  aren't saved: a loaded world puts the people back where the story says they are.
- **Acting a step out**: Work at the place's work spot (everyone working there a little to their own side), Rest at
  home (each person's own spot 12 to 26 m from the middle, the parts rack or the first spawn), a step with someone in
  mind next to them (a body, or a player here), and nothing else moves them. They walk, never run.
- **Meeting**: a step with someone in mind doesn't end until the two bodies are within 4 m (`StoryAgents.Meet`, the
  game's hook): the person waits a second at a time, up to 45 s past the step's time, then the step fails ("couldn't get
  to them"). Headless runs have no hook and keep the story's clock alone.
- **Sides**: people start no fights. Anyone may hurt them but friendly bots, and enemy bots hunt them as they hunt
  everyone (`SandboxEnemies`, one-sided). Hurt, they fight back or run ("Fights" below); otherwise they hear and see no
  fights, so they don't go and look. Clear all leaves them be.
- **A body killed** is the person killed: the appraiser's Kill (a fall or a blast of their own: Die) reaches the story,
  and they are dead for good ("Death" above). The body lies there a few seconds and goes, and they never get another.

The look over who has a body runs four times a second and allocates nothing once the bodies exist; the bodies
themselves are ordinary bots (the bot tick, the nav graph, the stuck watch, constructs as obstacles).

### Sight

What a body sees is `Source/Game/GameStorySight.cs`, in the same look. A body sees what is in front of it (about 70
degrees either side of where it faces) out to 40 m, and someone lying down out to 25 m, through no walls, smoke or
foliage (the bots' own sight line); anyone within 4 m it senses whichever way it faces. Players, enemy bots and anyone
lying dead are seen; living people are left to the story's own meetings, and friendly bots to themselves. Someone
unseen for a minute counts as seen anew. What it sees, it reacts to, one thing at a time, the strongest first:

- **Keep away** from someone it `Fears` (20 or more) or holds a `Grudge` against (30 or more), someone who hurt it since
  the last look, or an enemy bot: it backs off to 14 m, looking back now and then, and stays wary while they are in
  sight (5 s after). This takes over from anything but leaving by the road. Line: `Wary`.
- **Come and look** at a stranger (a player it doesn't `Know`) or at someone lying dead: it walks up (2.5 m, 1.5 m to the
  dead), stays 4 s and goes back to what it was doing, at most once in 45 s and only when free (idle, or working or
  resting alone: never on the road, in a play or on a step with someone). Lines: `Look` as it sets off, `Found` on
  reaching the dead (`?them` is the dead person, or the player).
- **Greet** someone it `Knows` or `Likes`, seen anew: it stops for a beat when free, turns to them and says a word.
  Line: `Wave`. A player who then walks up still gets the `Near` line.

A reacting body turns to whoever it reacts to (the bot's glance). The window's People tab shows a body's reaction.

**Witnessing by sight**: with bodies on, the story's events go by sight too (`StoryAgents.Sees`, the game's hook). A
person in an event's place learns of it only when they could see its actor or its target (a clear line out to 60 m,
whichever way they face: a commotion turns heads) or were within 12 m of one of them; the target always knows. Anyone
without a body here (a player away, a person headless) goes by the place, as before.

## Fights

A person's body that is hurt does something about it (`Source/Game/GameStoryFights.cs`, host, four times a second with
the bodies):

- **Fight back or run**, by who they are: a score of 0.7 Brave + 0.3 Vengeful + 0.3 their Grudge against the attacker,
  minus 0.5 their Fears of them, 30 less below 35% health, 8 either way by person and foe. At 50 or more they **fight back**:
  the body is an ordinary bot whose only enemy is that attacker (`SandboxEnemies` asks `StoryFightsWith`), with the
  pistol every body carries, heading for them when out of sight. Below, they **run**: they sprint 25 m at a time away
  from the attacker (at the map's edge, they cower). A fighter hurt below 35% may lose their nerve and run.
- **The call for help**: the one hurt says a `Fight` or `Flee` line instead of the Hurt one, and every other body within
  30 m that isn't in a fight already hears it. Someone who likes them (Likes 20 or more) and scores 45 or more (their
  Likes adds a fifth) comes to **help** them, saying a `Help` line; someone scoring under 35 within 25 m of the attacker
  **runs**; the rest carry on.
- **Over**: the attacker gone, dead or more than 60 m from a fighter; a fighter 20 s, a runner 12 s past the attacker's
  last hurt (and 30 m from them). Then the step they were on takes them again.

Every part of it is a verb the game records when it happens (Story/Verbs/People.json): **Attack** (someone hurt a person,
not killing them; once in 5 minutes for the same two: a grudge and less liking), **Fight** (they fought back: a
grudge), **Flee** (they ran: they fear the attacker) and **Defend** (they stood up for the `owner`: liked and owed for
it). Witnesses learn them like anything else (News.json: an attack seen makes people wary of the attacker, and a grudge
for someone they like), and three patterns tell them: *Stood their ground*, *Came to their aid* and *Turned the tables*
(a fighter kills the one who attacked). A kill by a person's body is an ordinary Kill. Enemy bots are attackers too:
each gets a person entity, as their kills do.

## Lines (phase 5)

The people speak (`Source/Story/StoryLines.cs` the rules, `StoryTalk.cs` what is said, `Source/Game/GameStoryTalk.cs`
when and who hears it; data `Story/Lines/*.json`). It is Valve's dialogue system (Ruskin's response rules): on an
*occasion*, of the rules whose criteria all hold, the one with the **most criteria** says one of its lines; ties are
drawn at random. A rule:

```json
{ "on": "Heard", "about": "Kill", "if": "?me Likes ?target >= 20, ?me Vengeful >= 60",
  "say": [ "Then {actor} has it coming.", "{actor} won't get away with that." ] }
```

- **on**: the occasion. Built in: `Step` (a step of theirs plays out with whoever it is with), `Saw` (something they saw
  happen where they are, said a moment later by up to two witnesses, not its actor or target), `Heard` (the answer to
  news told), `Near` (a player walks up to them), `Hurt` (shot or hit, not killed), `Radio` (a broadcast). By sight ("Sight" above): `Wave`,
  `Look`, `Wary` and `Found`, with `?them` the one seen. A rule's
  `"reply"` names the occasion the one spoken to answers on 2.5 s later (`Greeted`, `Chatted`, `Insulted`...: any word
  makes one, and its rules are those `"on"` it).
- **Criteria**, one each: `"verb"` (the occasion's verb: the step's, the event's), `"about"` (the verb of the news it
  is about: a Tell's, a broadcast's), and each clause of `"if"`, a query over `?me ?them ?actor ?target ?place ?here
  ?owner` (one more variable of its own, like `?x`, is allowed). For a step `?them` is whom it is with; for news
  `?actor` and `?target` are who did what to whom. A rule whose query or lines need a name the occasion doesn't have
  (no `?them` for a Hurt by nobody known) isn't said.
- **say**: up to 24 lines (printable ASCII, 110 characters), never the same twice running. They fill `{me} {them}
  {actor} {target} {place} {here} {owner}`, a first name with `.first` (`{them.first}`), and `{news}` (the event as a
  sentence).
- **chance** (said this often when it wins: it still wins, so it doesn't fall back to a vaguer rule), **every** (one
  speaker says it at most this often), **once** (once per speaker and listener this session).

A speaker's own words come at least 8 s apart (replies, what they saw, being hurt and the radio aside). Words change
nothing in the story: they only show it. They aren't saved.

**In the world** (host, Sandbox, four times a second with the bodies): a step's line is said when the body reaches whoever
it is with (or its spot; a trip as it sets off, never as it arrives); a player coming within 5 m of a body gets a
`Near` word once (again after walking 8 m off); a body hurt says so (3 s apart). A line said by someone with a body
reaches the players within 30 m: a chat line with the `Say` flag, drawn in cream and over the speaker's head for 6 s
(`UiChat.DrawSpeech`). Someone with no body here says it to nobody (the window still shows it).

**The radio**: a person with the `Radio` quality (Nell Ashby, at the landing) has the goal *On the air* (news that
hasn't been on the radio: the `"radio"` consideration) and the action `Broadcast` (`"radio": true`: at their work, it
takes the best news not aired lately, never their own sneaky deeds). Its event is like a Tell's (value the news, item
and owner who did what to whom), everyone in a place learns the news by radio and reacts to it, and the `Radio`
occasion's line goes to **every player** in the world as a `[RADIO]` chat line (`System | Radio` flags). What the radio
put out is worked out again from the chronicle on load. A new pattern, *On the air*, is a kill put out on the radio.
Players learn news this way, in the game, not from the Story window (Karl, 2026-11-04).

Wire: `ChatLine`'s flags gain `Say` (8) and `Radio` (16), protocol 145. Shipped: `Lines/People.json` (88 rules with
`Radio.json`: greetings, small talk, trade, news and the answers to it, insults and comebacks, giving and paying back,
work and travel mutters, what they shout at a killing or a theft, words for players by how they feel about them, being
hurt, fighting back, running and coming to help) and `Lines/Radio.json` (the bulletins by what the news is about). The Killing scenario with `--lines` shows all of
it headless.

## Talking to people

Players talk to the story's people (`Source/Game/GameStoryChat.cs` on the host, `Source/App/Program.StoryTalk.cs` and
`Source/UI/UiStoryTalk.cs` on each player's screen; lines in `Story/Lines/Talk.json`):

- **The panel**: the crosshair on a person's body within 3.5 m shows "Name  F: talk" (a construct part nearer than them
  keeps the use key), and the use key opens a small panel at the right of the screen and frees the mouse, like the
  Zombies trader's shelf. It shows what they said lately and four buttons: Hello, Any news?, Trade your (what you hold),
  Goodbye. Esc, Goodbye, walking 6 m off or their death closes it. Each button is `CActKind.Talk` (protocol 159): the
  host decides everything and the answer comes back as their own words, said aloud like any line.
- **Hello**: the first time, the story records a Greet (the player met them; answered with the `Greeted` rules), after
  that a Chat (at most every 2 minutes for the same two, so it warms them to each other like any Chat; answered with
  `Chatted`). Someone who fears the player says so.
- **Any news?**: the most interesting thing they know that the player didn't do and hasn't already had from anyone
  this session (not who told whom, not their own sneaky deeds), said as their Tell is ("Did you hear? ..."), and
  recorded as a Tell from them to the player. `NoNews` when there is nothing.
- **Trade**: only with a `Trader` (Jonas at the depot; Sol Reyes sells at the landing). The trader takes the part in
  the player's hands and gives one from Loot.json's `trader` table worth at most 80% of it, among the best they can
  afford (worth at least half of the best); worth is the material units of a part's Assembler recipe plus its parts'
  worth, else its mass. Recorded as the player's Trade. `NotTrader`, `EmptyHands` and `LowOffer` say why not.
- **Facing**: a body stops and faces a player who is talking to it until the panel closes, they walk off or 20 s pass
  with nothing asked (one player at a time: another is told who they are talking to); a body that says something to
  someone faces them for 4 s (and stands still for a player within 6 m); a body beside whoever its step is with faces
  them. The face is the bot's glance (BotMove.cs) held on them, so nothing in the bot code changed.

Nothing allocates in a tick; asking does (a line, a dictionary entry). What was told to whom and the Chat clock aren't
saved.

## Plays (phase 5)

Plays are short scenes cast from the world (`Source/Story/StoryPlays.cs`; data `Story/Plays/*.json`): the storylets of
box 5. Where a pattern notices a story after it happened, a play makes a small one happen: two who hold grudges meet and
it comes out, someone owed money asks for it, a friend of the dead raises a glass.

```json
{ "name": "About that money", "about": "someone owed money meets the one who owes it",
  "cast": "?b Owes ?a >= 10, ?a In ?p, ?b In ?p, ?a is Person, ?b is Person", "every": "1h30m",
  "beats": [
    { "by": "?a", "to": "?b", "say": [ "{b.first}. About that money.", "Don't think I've forgotten, {b.first}." ] },
    { "by": "?b", "to": "?a", "after": "3s", "say": [ "I know, I know.", "It's coming. Honest." ] },
    { "by": "?b", "to": "?a", "do": "Repay", "say": [ "Here. All of it. We're square." ] },
    { "by": "?a", "to": "?b", "if": "?b Owes ?a", "say": [ "That's what you said last week." ] } ] }
```

- **Casting**: `"cast"` is a query whose **first variable leads** (a person: the play happens where they are) and
  whose other variables are the rest of the cast; `"extra"` may cast more roles, who may be nobody (their beats are
  skipped). Everyone cast who is a person must be **free** (alive, in a place, in no other play, and idle or at a step
  with nobody else in it) and where the lead is; players and places are wherever the query says, and a role who is
  dead, like the departed in a toast, is only what the play is about.
- **Beats** come one after another, `"after"` the one before (4 s if not said). A beat has `"by"` and `"to"` (roles),
  says one of its `"say"` lines (filled with the roles' names, `{a.first}` for a first name, and `{here}`), or does
  one of the people's actions (`"do"`: Insult, Repay, Trade, Chat...; never a trip, the news or anything physical), or
  both. `"if"` (a query over the cast) and `"chance"` skip it; `"must"` breaks the play off when its action can't be
  done (otherwise the beat is skipped); `"then"` changes how they stand (effects like a pattern's, the roles as slots).
- **How often**: `"every"` is how often the play can happen anywhere (1 h if not said); `"once"` true plays it once per
  cast, or names the roles it counts (`"once": "?a ?d"`: each friend toasts each of the dead once). `"weight"` and
  `"chance"` decide between plays that can all be cast.
- **The director**: every 15 s of story time it gathers every play that can be cast, draws one by weight and starts it.
  At most 4 run at once, one a place; a place has none for 2 minutes after a play ended there or after something big
  happened there (news of interest 50 and up: a toast doesn't start a second after the killing).
- **While it runs** its people finish the step they were on but start no other (`AgentWhy.InPlay`: the People tab says
  which play). With bodies, a beat between two waits until they meet, as a step does (up to 40 s, then it is broken off),
  and their bodies go beside whoever they play with. A play is broken off when someone in it dies or leaves the place.
- What a play does is the people's own actions, recorded the same way (an Insult in a play is an Insult in the chronicle,
  with its grudges and news); what it says changes nothing, like any line. Its lines are said and heard as the people's
  are (`StoryTalk.SayFor`), so players within 30 m see them in the chat. Plays aren't saved: a loaded story has none
  under way.

Shipped (`Plays/People.json`, serious world, funny people): *Bad blood boils over* (a friend of both may step in), *About
that money*, *A toast to the departed*, *A word of warning* (about a killer), *Shop talk*, *Old friends*, *Request show*
(at the radio) and *Stranger in town* (a friendly person welcomes a player they don't know).

## Places

Places change with what happens in them, as Kenshi's towns do (`Source/Story/StoryPlaces.cs`; data
`Story/Places/*.json`). Every 10 s of story time (and a second after an event a state counts) each place is in the
**first** of the states whose terms all hold, or in none:

- `"recent"`: counts of a verb's events there lately (`{ "verb": "Kill", "within": "6h", "count": 2 }`);
- `"people"`, `"players"`: who is there now, living (`"0"`, `">= 3"`);
- `"if"`: a query over `?place` (`"?x Home ?place, ?x Dead"`);
- `"hold"`: a state lasts at least this long (5 m if not said), even when an earlier one would now hold.

A place in a state carries the quality of its name (declared in `Story/Relations/Places.json`), so goals, lines and the
radio can ask for it (`?here Mourning`; the people don't wander to a `Lawless` place). Coming to a state is a **Turn**
event at the place (its `"sentence"`: "Landing is in mourning"), and so is leaving one for none when it has an `"ends"`
("Landing is getting back to normal"): the people there see it and pass it on like any news, and the radio puts it
out. Shipped: *Abandoned* (someone who lived there is dead and nobody is there), *Lawless* (two killings in six hours),
*Mourning* (a killing in the last two hours), *Wary* (three thefts in two hours) and *Busy* (four trades in the hour
with people about). The state is a quality, so it is saved; what happened lately is read again from the chronicle on
load.

## Roster

The legacy roster (`Source/Story/StoryRoster.cs`) is everyone the story has known, people and players, the living and
the dead: where they are or how they died (who killed them, where and when), their **deeds** (the chronicle's events
they did, travel, joining and leaving aside), the **stories** told with them in (the sifter's), and a line to remember
them by. **Renown** orders it: a story counts 5, a deed as much as it is news (a killing 8, a theft 7, a trade 1).

The line is the response rules' `Remembered` occasion (`Story/Lines/Roster.json`), said by nobody: `?me` is the one
remembered, `?actor` their killer, `?place` where they died, `?here` their home or work; the rule with the most criteria
wins, the same line each time ("Killed by Karl at Landing. Not forgotten."). The roster is made when it is looked at
(the window, five seconds of story time apart; `--roster`) from what the story already holds: nothing is kept for it.

## Drama (box 3)

The drama manager (`Source/Story/StoryDrama.cs`) is Awash's Influence Mode: it doesn't write stories, it helps the ones
already under way finish. The sifter's live partial matches are its candidates; for each, the stage it waits for names
who would act (bound, or anyone: then up to two people who know of the story's last event) and whom to.

- **Ranking**: a story's score is its pattern's `"drama"` (1 by default, 0 leaves it alone) times its next stage's
  `"nudge"` (likewise) times how far along it is (0.5 at the start to 1 a stage from the end), how new (a pattern told
  often counts less: 1 / (1 + 0.25 per time told)), whether players are in it (1.5), how pressing (0.8 to 1.2 as its
  time runs out) and how ready the person is (their best goal that could take the verb, half again at a score of 0.5).
  A story waiting for a player, for a verb people can't take (Kill: people fight nobody), for someone nobody knows of, or for
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
--drama=compare --runs=12` tells 371 stories with the drama manager against 325 without (Bad blood 94 against 63,
Thief found out 164 against 153), with about two fifths of its directions followed by their person and a quarter done
by someone else first. A dead person gets no direction.

## Pacing

The drama manager also paces the stories (`Source/Story/StoryDramaPacing.cs`), after the build-up, peak and relax of
game directors, so big stretches are followed by quiet ones:

- **Tension** rises with what happens to the people: each event they are in adds its news interest / 100 (a killing
  0.8, a theft 0.7, an insult 0.4), and each story told with them in adds its pattern's drama x 0.5 a stage. It halves
  every 20 minutes.
- **A quiet stretch** starts once the tension passes 7 (the peak), and no sooner than 20 minutes into a building
  stretch: no stage directions and no strangers, so the people go their own way and things settle. It ends when the
  tension is back under 3 and 10 minutes went by, or after 30 minutes whatever the tension (a cast that keeps itself
  busy). The Drama tab counts the stories it held back.
- **Building**: the nudges come back at 40% of the strength and build up to all of it over 20 minutes.
- **Strangers** (the cast's `"strangers"`: name, about, qualities, feelings towards the cast's people, and `"suits"`,
  the verbs they would join a story for; each comes once). While building, the best story in progress that waits for
  anyone (Tell, Travel, ...) with nobody here who knows of it or would take it up may bring one in: someone who suits its
  verb sets off down the longest road into the place where the one the story needs is (else where it last happened),
  having heard of it on the way (their knowledge says "heard it on the road"). They make that place their home, start
  short of Company (new in town: they greet people, so Took them in can start) and are people like the rest from then
  on, nudged like anyone. After 45 minutes with nothing to nudge, one with history here (feelings first) comes into the
  busiest place. One stranger at most every 30 minutes, the first no sooner than 15 minutes in. In the host's world they
  walk in from the road's end like anyone arriving.
- Nothing of it is saved: tension starts afresh after a load. The Frontier cast has five strangers (Wren Halloway the
  tinker who trades in news, Ines Barlow the preacher, Cass Wilder come to collect from Ivy, Otto Venn back for Hugo,
  Dell Marrow the light-fingered prospector).

Measured with `--drama=compare --runs=12` on Frontier: 367 stories with pacing against 382 at full strength all the time
and 354 without the drama manager. It trades some squabbles (Bad blood 71 against 84) for the big stories (Word gets
around and Fled the killing 24 each, against 17 without pacing: strangers bring word of the killing), with about an hour
and a half of each run quiet and about three strangers a run.

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
outlives data changes: what no longer exists is left out. A place's state is one of its qualities, so it is saved too. A story that doesn't read is left out and starts afresh; the
world still loads. Without a world (plain Sandbox), the story lives for the session.

## Costs

`--story-sim` on 30,000 random events among 60 players and 120 constructs (every pattern's pool full): 11 us an event
on average, including the stories it completes. The Frontier scenario's twelve people: a think takes about 25 us on
average (scoring every goal and planning the best); an hour of host ticks with them costs about 90 ms in all and
allocates nothing but what the people say (a few KB an hour); with the plays and the places looked at as well, about
100 ms an hour. A drama rank over the Frontier stories takes about 45 us, its pacing included. The save holds each person's needs, goal, step under way, goal times and knowledge (about 40 KB
with 400 events); a plan is made again after a load. The window costs one bool check closed; open, its graph layout is
O(n^2) over the entities shown (at most 400).

## The IT Department's fax

The IT Department (a faction: its vehicles are the vehicle kit's IT style) is reached one way only: a **Fax Machine** and a
**Computer** (Constructs/Parts/Office, from `tools/constructs/office_parts.py`; IT vehicles carry them and its loot drops them).
Code: `Source/Constructs/OfficeParts.cs` (the parts and their panels), `Source/Game/GameSandboxOffice.cs` (the server),
`Source/Story/ItDesk.cs` (the words); data `Story/Office/IT.json` (checked by `--check-story`).

- **Plugged in** means on a construct with a battery, where a working battery's links reach the part and there is charge
  (docs/sandbox/CONSTRUCTS.md, "Power"). A construct with no battery runs its parts unpowered, but not these: an IT truck's
  fax is dead until a player takes it home and plugs it in.
- **The fax** counts the seconds it has been plugged in. Its first page comes a seeded time within the hour (`"first"`:
  10 to 60 minutes), the rest of the `"pages"` one after another (`"every"`). A page due while nobody is within 30 m waits
  for someone, then the fax springs to life: each line is a `[FAX]` chat line for the players near it, and its panel shows the
  page. What a fax has printed is its own and saved with the world (`CPart.State` the page shown, `Rounds` the story's step,
  `Count` the seconds towards the next).
- **The computer**: a player within 2.5 m of a plugged-in Computer who types in chat sends an email instead (only they see it
  go, as `[EMAIL]`). The first reply rule with one of its words in the email answers (`"replies"`, specific ones first, the one
  with no words last), `"replyIn"` later, on the nearest plugged-in fax within 150 m of the computer, waiting up to 30 minutes
  for one. With no fax near it says so (`"noFax"`).
- Nothing new goes on the wire: the lines are system chat lines, and a client's fax panel shows the last page it heard.

- **Malware**: when the scammers (below) hook a computer, IT notices and faxes its `"malware"` page to the nearest fax,
  with the cure.

- **Standing** (living factions, "Factions" below): the IT Department is a faction in the story world, and what it faxes
  follows what it remembers of the reader. A page may have `"alts"`, each with a `"when"`: a story query over `?us` (the
  IT Department) and `?them` (the reader's group); the first that holds prints instead, the most specific first. A reply
  may have a `"when"` too (a rule with words and a `"when"` answers only when both hold; rules with no words but a `"when"`
  come before the plain last one). `"notices"` are pages about something a player did: `"on"` names one of the faction's
  deeds or tiers, and the first whose `"when"` holds is faxed to that player, wherever they are, on the nearest plugged-in
  fax, a reply's time later (waiting up to an hour for them to come near one). Who reads a story page is the fax's owner
  when they are near, else the nearest player, and the first page is the IT Department noticing its fax (a Keep).
- Page ids stay as before (the story's pages, the replies, the malware notice), with the alternatives and notices after
  them, so a fax saved before keeps its page. `--check-story` refuses a notice on no deed or tier.

## Factions

A faction (living factions, F1; code `Source/Story/StoryFactions.cs`, the game's side `Source/Game/GameStoryFactions.cs`, the
Story window's *Factions* tab `Source/UI/UiStoryFactions.cs`; data `Story/Factions/*.json`, relations in
`Story/Relations/Factions.json`, verbs in `Story/Verbs/Factions.json`) is a Faction entity in the story world. The IT
Department is the first (`Story/Factions/IT.json`); the Call Centre (`CallCentre.json`), Clan MacSlop (`MacSlop.json`) and the
Ministry of Slop (`Ministry.json`) are data in the same shape.

- **Who they are**: a `"home"` (a story place, the faction's `Home`) and `"members"`: story people, `MemberOf` the faction,
  with traits as a cast's people have. For now they have no minds or bodies of their own: visits on foot come later (F3).
  `"staff"` names people of a cast who are its members too, whenever they are in the story (the penal colony's warden and
  guards, `Story/Casts/PenalColony.json`, are the Ministry's): greeting, chatting and trading with them count as deeds.
- **What is theirs**: the faction itself, its members, and the constructs it `Claims`: a construct the story meets with a
  name that starts with one of `"claims"` (`"IT "`: every IT style vehicle and blueprint, spawned from the parts menu or by
  the vehicle kit; the game reads the name when the construct is set down, `Game.StoryConstruct`). A scenario's construct
  is claimed by its own name, and a scenario's faction or member of the data's is the story's own.
- **Wants** (`"wants"`): qualities of the faction from 0 to 100 that start at `"start"` and drift by `"perHour"` (once a
  story minute). The IT Department wants Hardware, Respect and Uptime. Deeds move them; pages can ask about them.
- **Deeds** (`"deeds"`): an event of `"verb"` whose `"theirs"` role (the target unless named: target, owner, place, item)
  is the faction's, done by a player. It adds 1 to the `"remember"` relation the faction holds towards the doer and their
  group (`WreckedOurs`, `LootedOurs`, `KeptOurs`, `Courtesy`, `Threats`, `KilledOurs`), moves the faction's standing with
  both by `"standing"`, and its wants by `"wants"`. An Email deed asks for `"words"` (any one, any case) in the email;
  `"every"` counts a deed once in that long per group. The IT Department counts wrecking its vehicles (-20), stripping
  them (-8), plugging in its fax (+5), polite emails (+3, once in ten minutes), threats (-10) and rudeness (-4). A deed
  with `"whose"` is against another faction's things instead: Clan MacSlop likes you wrecking the Ministry's vans (+10).
- **Standing** runs from -100 to 100 and is held as the faction's `Likes` (above 0) or `Grudge` (below) towards a party,
  so queries, patterns, the drama manager and the fax read it like any feeling (`?us Grudge ?them >= 16`). `"tiers"`
  name its bands from the worst up (IT: Blacklisted below -50, Flagged below -15, Unregistered, Registered from 15, Admin
  from 50); coming to a new one is a Rate event ("IT Department now rates Rook: Flagged"), and the desk can fax a notice.
- **Groups**: standing is kept for a player's group: the player faction they are in (`Source/Constructs/Factions.cs`,
  a Faction entity `"group:NAME"` the game keeps them `MemberOf`, once a second), or themselves when in none. A deed
  counts towards both the doer and their group; a group's is the one the fax reads.
- **Rivals** (`"rivals"`: `{"Clan MacSlop": 50}`): a rise in a faction's standing takes that share (percent, a point at
  least) of the rise off each rival's standing with the same party. The Ministry and Clan MacSlop are rivals both ways, so
  are the Ministry and the Call Centre; the IT Department and the Call Centre a little (25%).
- **Notorious** (the world-wide state on top of each faction's own): an Atrocity event (the game records one when an
  Antimatter Warhead goes off with anyone but its owner in its reach, `GameStoryTech.cs`; one set off in an empty valley
  is only a very big bang) makes the doer and their group `Notorious`, a quality every faction
  reads: their standing with all of them is -100 whatever it was, so every faction hunts them. Each faction gets a
  notice on `"Notorious"`, and pages can ask `?them Notorious`.
- **Verbs**: Email (a player at a Computer to a faction; its effect counts `Emails`), Fax (a faction's page printed for a
  group: `Faxed`, which "per my last fax" reads), Keep (a player plugs in a faction's fax: its first page), Rate; and between
  player factions (GameStoryFactions.cs): Unite (a player founds one: actor the player, target its group), Ally, Feud (the
  target's group takes a Grudge) and Truce (actor and target the two groups), which the radio has lines for. The Faction
  window (F2) shows each player faction how every story faction stands with it, and whether it is Notorious.
- **Strip**: a wreck's part taken off is noted whoever owns it, and the story keeps a Strip of one's own or nobody's
  construct only when a faction claims it (stripping the IT van you spawned yourself still counts against you with IT).
- Everything is world state (entities, relations, qualities), so it is saved with the story; a faction new to the data
  is put in an old save's story when it loads. Notices waiting to be faxed are not saved.
- **Trying it**: `--story-sim Story/Scenarios/ITDepartment.json` plays a fax kept, two vans wrecked and stripped, and
  prints the IT Department's standing at the end; in the game, the Story window's Factions tab shows each faction's
  wants, members, claims, and its standing and memory of every player and group.

## Technologies

No money: standing is what a faction gives you things for, and what it gives is technology (code: the engine's side in
`StoryFactions.cs`, the game's `Source/Game/GameStoryTech.cs`). A faction's `"teaches"` lists a few technologies, each a
`"name"`, the `"tier"` it is taught at and the construct `"parts"` it holds (eight technologies at most). In survival an
Assembler won't build a part a faction teaches until its owner has been taught it (the look line and the machine's screen
say "not taught" and who teaches it, `AsmBlock.Untaught`); a part no faction teaches is built for anyone, and creative
builds everything. The first loop:

1. Earn standing with deeds (above; rivals cool as you go).
2. The first time a faction's standing with a party comes to a technology's tier (the Rate event's moment), it teaches
   them every technology of that tier and below: they hold `Learnt` towards the technology (a Thing, `"tech:FACTION:NAME"`),
   for good, whatever the standing does after; a Notorious party is taught nothing.
3. A Teach event goes on the record (the radio has lines for it), the players taught get a HUD line ("Ministry of Slop
   taught you Explosives Licence: your Assemblers build ..."), and a desk can fax a notice named after the technology (the
   IT Department's are in `Story/Office/IT.json`).

A party is a player's group (their player faction) or the player alone; an Assembler builds a part for its owner when the
owner or their group has learnt it. Joining a group (or loading a save from before technologies) teaches what the
standing already earns. The Faction window (F2) lists what each faction teaches, and with a faction of yours which it has
learnt. What ships:

| Faction | Tier | Technology | Parts |
| --- | --- | --- | --- |
| IT Department | Registered | Remote Control | Remote Transmitter, Medium Remote Transmitter, Remote Console, Drone Receiver |
| IT Department | Admin | Video Network | browser, video and camera screens, all sizes |
| The Call Centre | Valued Customer | Wireless | Wireless Sender, Wireless Receiver |
| The Call Centre | Premium Member | Signal Tricks | Radio Homer, Beacon Homer, Command Link, Flare Dispenser, Smoke Shell |
| Clan MacSlop | Friend of the Clan | Pipes and Drums, Clan Plate | Medium and Large Speaker; Heavy Armour and its port |
| Clan MacSlop | Clansman | The Wrecking Ball | Wrecking Ball |
| Ministry of Slop | Trusty | Explosives Licence | Warhead Blast, Warhead Bunker, Aerial Bomb, Mine Dispenser |
| Ministry of Slop | Contractor | Atomic Licence | Small Nuke, Fission Warhead, Fusion Warhead |
| Ministry of Slop | Crown Servant | The Antimatter Bomb | Antimatter Warhead |

How standing is earned for now: IT as above; the Call Centre likes anyone who falls for one of its adverts (a Hooked
event, +12) or wrecks a Ministry van (+6); Clan MacSlop likes wrecking (+10) and stripping (+3) Ministry vans; the
Ministry likes a good morning to its staff (+2), a chat (+1, once in ten minutes), a trade (+3) and wrecking the clan's
or the Call Centre's vans (+8). Killing, wrecking and stripping a faction's own costs as with IT. A part name that isn't
a part is logged when the game first reads the table (`[story] ... no part called`). Trying it:
`--story-sim` with a scenario that wrecks two Highland vans prints "Ministry of Slop taught Karl Explosives Licence", and
Karl keeps `Learnt` after wrecking the Ministry's own vans takes him to Enemy of the State.

Next (the living factions plan): F2 voices for the other factions (Call Centre scams that remember who fell for them,
Ministry memos, MacSlop challenges on the radio), F3 visits on foot through the drama manager, F4 turf on planetoids.

## The Scammers

The Scammers (The Call Centre) live in the Computer's adverts. Code: `Source/Story/ScamDesk.cs` (the words),
`Source/Constructs/OfficeParts.cs` (the screen, the click), `Source/Game/GameSandboxScams.cs` (the server); data
`Story/Office/Scammers.json` (checked by `--check-story`).

- **Adverts**: a plugged-in Computer's screen holds two touch buttons (`office.advert`, `office.advert2`; docs/sandbox/CONSTRUCTS.md,
  "Touch panels") under the IT text. Which two of the `"adverts"` a computer shows comes from its place on its construct
  (`OfficeParts.Seed`), so every peer sees the same ones.
- **Hooked**: clicking one (Hands out and empty) hooks the computer with that advert's scam: `CPart.State` the scam plus one,
  `Rounds` its messages sent, `Count` the seconds towards the next, all saved with the world. The scam's first message comes in
  `"firstIn"`, the rest every `"every"`, as `[INBOX]` chat lines for the players within 10 m (it waits for someone). The screen
  turns red with the scam's `"popup"` and its latest message. The IT Department notices (its `"malware"` fax).
- **Typing back**: a player at a hooked computer emails the scammers, not IT. The first reply rule with one of its words
  answers, `"replyIn"` later. A rule with `"take"` means the player fell for it ("ok", "yes", a gift card, a code): the scammers
  take that share of the computer's construct's battery charge, say so as a `[PC]` line, and are gone.
- **The cure**: IT's own advice. A hooked computer that loses power (unplugged, its battery flat or shot) is cured. A scam
  that runs out of messages closes by itself.
- Nothing new goes on the wire. A client's screen shows the scam on the computer its own player clicked, with the messages it
  heard; a `[PC]` line ends it there.

Its vehicles are the vehicle kit's Call Centre style (docs/vehiclekit/VEHICLEKIT.md, "The Call Centre style"). Next: scams
that answer what the player says more closely, and a Call Centre that remembers who fell for it.

## Next

- **Phase 2** (done): people in the story world, headless. Next for them: news from radios, more casts, factions with minds of their own (factions themselves: "Factions").
- **Phase 3** (done): the drama manager nudging the people, pacing them (quiet stretches between big stories) and
  bringing strangers in to join stories in progress. Next for it: strangers who leave again, tension per place (today it
  is one for the whole world), and casting plays for its stories.
- **Phase 4** (done): bodies in the host's world, and death for good, and fights (a person hurt fights back or runs and
  calls for help). People notice players by sight ("Sight"), and players talk to them, who face whoever they talk to
  ("Talking to people"). Next for them: hearing gunfire and footsteps, people who start a fight of their own (a grudge
  settled with a gun), drives and hauls as people's verbs, and more than one world place (other planetoids as places).
- **Phase 5** (done): presentation. The people speak by response rules, and the radio (5a); plays, places that
  change, the legacy roster (5b). Later: a radio part players build and tune (today every player hears the radio),
  voices, memorials in the world for the dead, the drama manager casting plays for its stories, milestones from
  mining and making as plays.
- Clients don't see the story window: they hear the people and the radio (chat lines).
