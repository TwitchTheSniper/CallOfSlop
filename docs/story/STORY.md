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
    and what it did lately. *Nudge the people* turns it off and on; the slider sets its strength.
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
  and prints each pattern's stories both ways. `--strength=X` sets how hard it nudges (0.6 by default).
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
  people (home, work, qualities as traits from 0 to 100, Wealth, Trader) and starting feelings. A scenario names one
  with `"cast"` (and `"run"`: how long the clock goes on after its last event, `"seed"`: the people's random seed).

The shipped data: `Life.json` (needs, eleven actions, the tasks, twelve goals from Rest and Work to Get even and Keep
away), `News.json` (interest and reactions), the Frontier cast (twelve people in Ridge Camp, The Depot and Landing,
with a few grudges, friendships and a debt), `Patterns/People.json` (Word gets around, Thief found out, Thief
confronted, Debt repaid, Bad blood, Bit the hand that fed, Took them in, Fled the killing: someone who now fears a
killer leaves the place soon after) and `Scenarios/Frontier.json` (two players passing through, four hours after);
`Scenarios/Killing.json` has a player kill Rhea Lund at the landing. A scenario with a cast may name the cast's people in
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
- **Sides**: people fight nobody. Anyone may hurt them but friendly bots, and enemy bots hunt them as they hunt everyone
  (`SandboxEnemies`, one-sided). They hear and see no fights, so they don't go and look. Clear all leaves them be.
- **A body killed** is the person killed: the appraiser's Kill (a fall or a blast of their own: Die) reaches the story,
  and they are dead for good ("Death" above). The body lies there a few seconds and goes, and they never get another.

The look over who has a body runs four times a second and allocates nothing once the bodies exist; the bodies
themselves are ordinary bots (the bot tick, the nav graph, the stuck watch, constructs as obstacles).

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
  news told), `Near` (a player walks up to them), `Hurt` (shot or hit, not killed), `Radio` (a broadcast). A rule's
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

Wire: `ChatLine`'s flags gain `Say` (8) and `Radio` (16), protocol 145. Shipped: `Lines/People.json` (78 rules with
`Radio.json`: greetings, small talk, trade, news and the answers to it, insults and comebacks, giving and paying back,
work and travel mutters, what they shout at a killing or a theft, words for players by how they feel about them, being
hurt) and `Lines/Radio.json` (the bulletins by what the news is about). The Killing scenario with `--lines` shows all of
it headless.

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
100 ms an hour. A drama rank over the Frontier stories takes about 35 us. The save holds each person's needs, goal, step under way, goal times and knowledge (about 40 KB
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

This is the channel; the IT Department isn't a story entity yet. Next: an IT Department faction in the story world, faxes
and emails as events its patterns and the drama manager can use (a reply that depends on what the player did to its
convoys), and pages that wait for an answer.

## Next

- **Phase 2** (done): people in the story world, headless. Next for them: news from radios, more casts, factions.
- **Phase 3** (done): the drama manager nudging the people. Next for it: casting someone new into a story (a stranger
  arrives), pacing (quiet stretches between big stories).
- **Phase 4** (done): bodies in the host's world, and death for good. Next for them: people who notice players (seeing
  is still by place, not by sight), fights, drives and hauls as people's verbs, facing whoever they talk to, and more
  than one world place (other planetoids as places).
- **Phase 5** (done): presentation. The people speak by response rules, and the radio (5a); plays, places that
  change, the legacy roster (5b). Later: a radio part players build and tune (today every player hears the radio),
  voices, memorials in the world for the dead, the drama manager casting plays for its stories, milestones from
  mining and making as plays.
- Clients don't see the story window: they hear the people and the radio (chat lines).
