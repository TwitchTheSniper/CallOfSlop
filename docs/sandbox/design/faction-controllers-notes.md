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

### Merge review fixes (2026-11-17)

- Stray fire: a hit on a power by a power it holds a pact with or counts as a friend is forgiven (no deed, no pact broken, no
  fire back) up to `pacts.friendlyFireHits` (8) hits in a minute; a minute with none starts the count again; past it the fire
  is deliberate and an attack (`PowerRelations.StrayFire`, gated in `Game.Deed`).
- A body a player owns, or a prize, is its owner's doing whatever crew works it (`Game.CrewDoer`).
- A powers section that doesn't read resets only the grid, pacts and news; standings with players (the Crew section) stay.
- Landing: `Land` saves the whole world (`WorldSaveNow(wait: true)`, POWR included) before reading the people's sections
  back, so nothing is lost. A kept place for travel (`GameTravelPlace`) starts from the ship file's copy and keeps its own
  relations from then on: the host's main game and a kept place don't share them yet (phase 3's single controller layer).

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

## Phase 2: groups and admirals (built 2026-11-17, branch `claude/faction-admirals`)

Built to the user's answers on admirals (with Karl): an admiral is a role in the hierarchy, not a combat post; it leads any group of
captains, combat or civilian.

### What a player gets

- NPC ships work together. A group is 1 to 12 ships of one power under an admiral (256 groups at most); a spawner's crewed convoy is one
  (its leader carries the admiral). The admiral weighs the strength it has left against the enemy's every think (no fixed break line:
  it pulls back under half the ratio its doctrine wants and comes in again past it), focuses its warships' fire on one target (a skilled
  admiral picks the one worth most for what it takes to kill, a poor one the nearest), and with civilians to keep it screens them: armed
  ships between the flagship and the threat while the flagship keeps going.
- Orders a captain obeys (`OrderKind`): Form (a slot in the flagship's frame), GoTo, Engage (focus fire), Escort, Hold, Retreat, Board
  (closes to 40 m and holds alongside), Screen, and the civilian Load, Unload, Mine, Deliver and Route (a route of up to 8 points, each
  with its work and a dwell, looped). Captains keep their own judgement inside an order: their Flee lines are held off by the doctrine's
  discipline (a Militia line holds; Raiders run when they like).
- The fleet command network: the admiral commands from whatever seat it has on any ship of its group that is on the net. A ship reaches
  the net by radio (25 km) while a part that gives access is whole: Wireless Sender or Receiver, Long Range Sensor, Wi-Fi Mast, Router
  Antenna or any radar (`Crew/Fleet.json` "net.parts"); with none, its crew's hand sets reach 1.5 km. Ships hear the admiral through
  each other (a chain). Cut off (net parts shot away, out of reach), a ship keeps its last order 30 s, then its captain is on its own; an
  admiral whose own ship is cut off reaches only those within its own reach.
- Losing the admiral (killed, taken with its ship) runs the doctrine's succession: `captain` (after the delay the senior captain on the
  strongest ship steps up: news), `break` (every ship runs from what it fought for a minute, the group is gone), `flagship` (only a ship
  with a radio fit can carry an admiral; none: home).
- Names: each crewed ship has a captain (the senior member: at the helm first). The look line reads "Admiral Una's flagship, Militia
  group 3, Captain Bea, crew of 5"; news lines name admirals and captains.
- Prisoners: a ship taken with its admiral or captain aboard makes them officers: -25 with their power (`captureOfficer`), a notice of
  their ransom (1,500 an admiral, 400 a captain). In the Crew window a prisoner admiral or pilot has Question and Ransom; ransomed or
  released, +15 (`releaseOfficer`, in place of a plain release's +5). Questioning takes about 60 s at resistance 0.5 with an
  Interrogator of skill 128 (skill 255: two thirds; alone: three times), resistance by doctrine give or take 0.2 by the person. Broken,
  an admiral gives up its group (each captain, crew and order, positions) and the other admirals of its power (positions); a captain its
  ship, its crew and where any of them are off it and what they do. Positions show as labels over the world for 5 minutes, to the asker
  and their faction; a hard one (over 0.8) gives up only two. An own hand becomes your Interrogator from the Crew window (job
  `Interrogator`).
- A boarded group ship's call for help goes to its admiral first: every ship of its group on the net marks the boarder a foe.

### Where

| What | Where |
| --- | --- |
| Groups, admirals, net, orders' choice, succession, reports ring (64, drops counted and logged), intel, Fleet section | `Source/Crew/CrewFleet.cs` |
| Doctrines and the net's numbers (`call-of-slop-fleet/1`; missing: built-in) | `Crew/Fleet.json`, `Source/Crew/CrewFleetRules.cs` (a kind or power may name one with `"doctrine"`) |
| Orders at the helm (fly to a slot or place, drive one, screen, retreat) | `Source/Crew/CrewOrders.cs` (ShipAi partial), hooks in `ConstructShipAi.Step` and `CrewGround.GroundHelm` |
| Engage and Board targets, discipline, reports from captains | `CrewWorld.Drive`, `CrewWorld.Captain` |
| Convoys as groups, officers, questioning, ransom, intel marks | `Source/Game/GameCrewFleet.cs`; buttons `UI/UiCrew.cs`; labels `Program.NameTags.cs` |
| Tool | `--crew-check --crew-fleet strike|screen|succession|convoy|mining|interrogate|all`, `--crew-fleet scale --fleet-scale N --fleet-awake K [--group-size G] [--crew-scale-ship NAME]` (`Source/Crew/CrewFleetCheck.cs`); window verbs `crewfleet`, `crewofficer question|ransom` |

### Saves and network

- Saves: a `FLET` section (`WorldSaveWriter.FleetTag`, version 1): each group's header (power by name, task, doctrine by name, admiral,
  number, order, route). Each body's crew block (version 3) ends with its group's slot and its captain; ships rejoin their group as their
  crews load (a loaded group waits 20 s before it thinks). An order's body (Escort, Board) isn't kept. Officer prisoners are plain
  prisoners after a load (not kept yet). The crew block's load capped jobs at Loader (a Spotter, Crane, Hauler or Builder came back a
  Loader): fixed.
- Protocol 345 (main 344 at the merge): `Msg.CrewSeats` ends with u16 captain's name, u16 admiral's name aboard, u16 group
  number; `Msg.Intel` at the end of `Msg`; `CrewOrder` gains Interrogate, Ransom, Interrogator.
