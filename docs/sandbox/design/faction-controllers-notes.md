# Faction controllers: builders' notes

Notes kept while building `faction-controllers.md`, phase by phase. The design and the user's answers there rule; this says what
was built, where, and what is left.

## Phase 1: living relations (built 2026-11-17, branch `claude/faction-relations`)

### What a player gets

- The world's factions fall out and make up through play. Hitting, boarding, capturing a power's ship, killing its crew or
  destroying the ship sours that power on the doer (a player or another power); releasing prisoners, hiring and helping in a
  fight warm it. Powers whose crews see it happen take a part: a friend of the victim's takes its loyalty's share, an enemy
  of the victim's warms to the doer by a fifth.
- Grudges fade towards each relation's rest value on real (simulated) time; nothing moves while the game is off. A war
  never fades on its own: it takes a ceasefire.
- Pacts form and break by themselves between powers: a ceasefire when one side's losses pass its pride line (or a long war
  that saw fighting goes quiet), a non-aggression pact for two powers neutral or better with a common enemy, an alliance for
  two friends with a common enemy at war (both loyal enough). An attack breaks a pact, and those who know think less of the
  breaker; an alliance falls apart when either drops under Neutral, a non-aggression pact when either goes to war.
- Crews fight by the grid: on sight below 0 (as before), never while a pact holds, and they answer a crew that shot them.
  A crew joins a fight it sees against anyone fighting a power it counts as a friend or ally, or a person it counts as a
  friend (standing 60 or more): allies help players and each other (answer 9, as far as phase 1 goes: no pacts with players
  yet). A boarded crew's call for help is answered by its friends' and allies' crews too.
- The Faction window (F2) shows "Between the factions": the grid in words (hover for the number; Player Crew and Test
  Hostile, which keep their relations, are left out), the pacts with minutes held and left, and the last news lines.
  "How the world's factions see you" uses the same words (at war, hostile, wary, neutral, friendly).

### Where

| What | Where |
| --- | --- |
| Powers (`Crew/Powers/*.json`, `call-of-slop-npc-power/1`; none: one power a kind, so worlds play as before) | `Source/Crew/CrewPowers.cs` |
| The engine: grid, bands with hysteresis, events, third parties, help, decay, pacts, news, save bytes | `Source/Crew/CrewRelations.cs` (`PowerRelations`, `RelationRules`) |
| Event amounts, bands, decay, pact numbers | `Crew/Relations.json` (`call-of-slop-npc-relations/1`; missing: the same numbers built in) |
| Server side: deeds from crews, who did it (a crew member's hands or a crewed body's guns count as its power), sight, news to clients, saves | `Source/Game/GameCrewFactions.cs` |
| Fight rule | `Game.CrewShipFights` / `CrewHelpsAgainst` (`GameCrew.cs`) over `PowerRelations.ShipFights` |
| Hit credit for nobody's craft | `ConstructWorld.DamageBody` -> `Construct.LastHitBody` (guns, rounds, shells' blasts, massive blasts) |
| Kills and lost ships | `CrewShip.DoerPlayer/DoerBody/DoerAt` (an attack's doer, 15 s for a kill, 60 s for a lost ship) |
| Window | `UiFactions.DrawPowerRelations` |
| Tool | `--crew-check --crew-relations [--no-net]` (`Source/Crew/CrewRelationsCheck.cs`) |

A power file:

```json
{ "format": "call-of-slop-npc-power/1", "name": "Rime Home Guard", "kind": "Militia", "tag": "RHG", "colour": "#4080FF",
  "parameters": { "aggression": 0.3, "greed": 0.2, "loyalty": 0.8, "forgiveness": 0.4, "pride": 0.6, "caution": 1.4,
                  "values": { "crew": 0.6, "ships": 0.5, "cargo": 0.3, "planets": 0.9 } },
  "relations": { "Red Hand": -70, "players": 0 },
  "rest": { "Red Hand": -30 },
  "controller": true }
```

Parameters default to 0.5 (the event's base amount: each scales it by 0.5 + p); a relation not named comes from the kinds'
stances (hostile -50, wary 0, neutral 20); `rest` defaults to the start. Two powers of one kind start at the kind's word for
itself (neutral). Ids: `CrewShip.Faction`, `Construct.NpcFaction`, `Crew.Fac` and `Msg.CrewSeats`' faction byte are power
ids now; the kind is `CrewPowers.KindOf(id)`. Saves keep names, and a name that is a kind finds that kind's first power.
A generated craft is crewed by the first power of its style's kind (as many powers of one kind as you like, the spawner
picks the first until phase 3 gives powers their own ships).

### Saves and network

- Saves: a `POWR` section (`WorldSaveWriter.PowersTag`, version 1): powers by name, the grid, pacts (seconds held and
  left), wars (seconds and losses), news. Standing with players stays in the Crew section, now by power name. No new save
  file. A save from before has no section: every relation starts from the files. It travels with the people on a landing.
- Protocol 333: `Msg.Factions` ends with the grid and the pacts; `Msg.PowerNews` (the last 32 to a joiner, new lines to
  everyone). Server authority: clients only show.

### Tried by hand

- `--crew-check --crew-relations`: every step passed (two Militia powers at war; a witnessed attack moving a third party,
  an unwitnessed one not; help +3 and its cap; a player's grudge -30 -> -20 in 20 minutes; a war held at its line; an
  alliance over a common enemy; the ally joining a fight against a crew fighting its friend, and against one fighting a
  friendly player; the alliance broken by an attack and the word spreading; a ceasefire after losses and its end; a save
  round trip; a host and a joined client: the client's grid, pacts and news match). The powers' thinking: 0.01 us a tick.
- A window run: host and joined client under xvfb, the client's Faction window shows the grid, pacts and news.
- `--crew-check --crew-faction Militia`: OK, as before. Self-test sections run by name: Packets, Save folder, World links,
  Paste and packets, Sandbox body sync, Construct weapons, Blast push, Sandbox spawns: all passed.

### Not verified

- A real fight between two powers' crewed ships in a running world (the check drives the engine and the fight rule, not
  crewed bodies shooting each other); a crew answering fire through `FoeBody`.
- A world written and read through the real save file (the section's bytes round trip in the check; the hook-up was read,
  not run). The landing copy of the section.
- How often default worlds (one power a kind) now see news: their pairs neutral with a common enemy sign non-aggression
  pacts at the start (three news lines); kinds that are "hostile" start at -50, the War band.

### Left for later phases, and open

- **A power wiped out (answer 5)** is not built: a power has no ships, crew or planets of its own until phase 3 (ships
  within its production) and phase 4 (planets); today the spawner makes crews of a kind from nothing, so "none left" means
  nothing yet. Proposal for then: a power with no planets, no ships and no crew left at its controller's think is gone
  (news, its row greyed, its relations frozen, kept in the save as fallen); open question for a later phase: whether a
  free row may be taken by a new power (an independent captain "making a name", SURVIVAL.md) and on what terms.
- Pacts with players (phase 5), Call allies, raids, trade, escort and planet events (phases 2-4: the event keys are not
  in `Relations.json` until something posts them).
- Aggression, greed and caution are read but used only by the ceasefire's answer (aggression); the rest wait for missions.
