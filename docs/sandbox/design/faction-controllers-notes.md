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
  their ransom (1,500 an admiral, 400 a captain). In the Crew window an officer prisoner (the roster's job byte has bit 7) has Question and Ransom; ransomed or
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

- Saves: a `FLET` section (`WorldSaveWriter.FleetTag`, version 1): the fleet's stamp (random per world), each group's header (power by
  name, task, doctrine by name, admiral, number, order, route), and the officer prisoners (uid, power, group slot and number, rank,
  resistance: a saved prisoner admiral can still be questioned or ransomed; a questioning under way starts again). Each body's crew
  block (version 3) ends with its group's slot, number and the fleet's stamp, and its captain; ships rejoin their group as their crews
  load only when all three match (a ship from another world's save, as on a landing, never joins an unrelated group in the same slot;
  a loaded group waits 20 s before it thinks). An order's body (Escort, Board) isn't kept. The crew block's load capped jobs at Loader (a Spotter, Crane, Hauler or Builder came back a
  Loader): fixed.
- Protocol 346 (main 345 at the merge): `Msg.CrewSeats` ends with u16 captain's name, u16 admiral's name aboard, u16 group
  number; `Msg.Intel` at the end of `Msg`; `CrewOrder` gains Interrogate, Ransom, Interrogator.

### Tried by hand (2026-11-17, the 2-core box shared with two other builders)

- `--crew-fleet strike`: three Militia Zero Days, two frigates (one shot about to 35%): all three on the shot-about one 12 of 12 looks, it
  fell to 30%. `screen`: a Traders convoy of three Zero Days, a threat to its north: both escorts screening between 8 of 8 looks, the
  flagship kept on its way. `succession`: Militia, Captain Una in command 9.2 s after the admiral was shot (delay 8 s); Raiders, the
  group broke 0.7 s after and all three ran. `convoy` (four Bandit ground vehicles as Militia, the leader the flagship): sent 300 m,
  followers 14 m off their places (as phase 2's check measures it), the leader there; a Zero Day 220 m off, the admiral pulled back.
  `mining` (three Traders Zero Days): mine, deliver, then back to mine. `interrogate`: the admiral gave up its group's two captains and
  the other admiral (three positions), a captain its ship and crew; resistance 0.76: 71 s with an interrogator of skill 200, 274 s alone.
- A window run (host and a joined client under xvfb, `crewconvoy Militia 3`, `crewfleet`): the spawner's convoy became group 1 under
  Admiral Vera on the leader, the other two joined with Form orders; the client's look lines read "Admiral Vera's flagship, Militia
  group 1, Captain Zed, crew of 5".
- Scale (`--crew-fleet scale`): 256 groups of 4 (1,024 bodies, 64 awake): the admirals 2.4 us a tick, worst 35 us, 0 bytes. 1,000
  Bandit Helicopter 1 (23 parts) all awake and driven in 84 groups (driver cap raised to 1,000): admirals 34 us, crews 0.81 ms, the
  world's own step 12.6 ms a tick (worst 55 ms): 13.4 ms, over the 8 ms budget. 100 Zero Day Frigates (276 parts) all awake: the world's
  step 78.5 ms a tick (about 0.8 ms a frigate), so about ten awake frigates fit in 8 ms today. The brain is not the limit; the physics
  of awake bodies is (and the world step allocated 166 KB in 300 ticks with 1,024 bodies: not the crews' or the admirals').

### Not verified

- Questioning, ransom and intel marks in a running game (no ship taken in a window run: only the core in the check); the Crew window's
  new buttons and the marks drawn (no picture); the Fleet section through a real save and load; a cut-off net in play (no part shot away);
  Board, Escort, Load and Unload orders in a run; a group's call for help.
- Formation flying is loose: followers 20 to 160 m off their slots in the air, and in the mining check two frigates settled onto the ground
  after 130 to 170 s of flying (cause not found: the height hold under orders, or fuel). Helicopters fly poorly under the crews' flying
  (Karl's `ShipAi`, also in the plain fight: their noses wander), so the air checks use Zero Days.
- The admirals' worst tick in the small checks was 2.4 to 5 ms once (taken for JIT or a GC on the shared box; at scale the worst was 35 us).

### Left (later phases, or follow-ups)

- **The 1,000-body target** needs the LOD the design asks for: a coarse physics and think rate for awake bodies far from players, batched
  drawing and interest management. Not built: the measurements above say where to start (the world step, then `CrewWorld.MaxDrivers`, 64).
- The relations scale widened (blood enemies to wants to merge) and the new pacts (trade, tribute, vassalage): not done here (they change
  phase 1's bands and the Faction window; they fit the controller phase better).
- An order's body kept through a save; admirals coordinating across groups (only intel names them);
  reports read by a controller (the ring fills and is counted; phase 3 reads it); players as admirals (phase 6).

## Phase 3: the controller in open worlds (built 2026-11-18, branch `claude/faction-controller`)

Built to section 3, phase 3's row, the user's answers (5: powers can be wiped out; 6: real production; 7: fog; 8: 1,000 bodies; 9: allies help)
and Karl's (2: the wider scale and more pacts; 4: existing ships as placeholders). Phase 2's leftovers folded in: the wider relations scale and the
new pacts (they need a power's stock, which this phase brings), and the controllers read the groups' report rings.

### What a player sees

- In a survival world, the powers act round the players. Each power with a plan lays its places on the open world (Traders a home and a second
  place facing it across the player, so their road passes by; Raiders a camp; the Militia a post: 1.2 to 2.2 km out, on ground, another bearing
  and nearer when a map's edge is in the way), and fields ships there, out of everyone's sight (600 m), bought at their real bill.
- **Convoys.** Every few minutes the Traders send a box truck with two technicals as escorts, under an admiral, from home to their other place
  (load, deliver: their stock earns the cargo), and back; at home each ship docks out of sight (600 m: 400 m) into the hangar, its hull kept, and
  is fielded again (its repair paid) next time.
- **Raids.** A power at war with the convoy's (Raiders with Traders) that knows of it (near one of its places, or seen by one of its crews: fog)
  sends raiders to catch it; its captains fight on sight and the admiral focuses them. A cargo ship lost to the raid is loot (half its value) and a
  `raidConvoy` event; then they go home with it, or when their admiral pulls back for long, or after 12 minutes.
- **Calls for help.** A convoy in a fight calls: its allies (alliance, vassalage) must answer or be thought less of (`refuseCall`), and a power
  neutral or better to the caller that fights its enemy answers when loyal enough (0.4): the Militia sends warships to escort the convoy (or hold where
  it called from), comes home when 40 s pass with no enemy, and gains `answerCall` and `escortConvoy` with the caller.
- **Places lost.** An enemy group sitting 300 m from a place with none of the power's ships near for 3 minutes takes it (news); its income goes.
  Its ships standing there with no enemy for as long take it back.
- **Gone for good** (answer 5): a power with no ship (out, building or docked), no place it holds and less stock than its cheapest ship is gone
  (news; the Faction window greys it "(gone)"; it thinks and signs nothing more).
- **The wider scale** (Karl's 2): a relation at -100 is a **blood feud** ("blood enemies", "BLOOD" in the grid): a ship destroyed, a capture or a
  raid against a power already at the floor (-99) whose pride is 0.4 or more; it never moves again (no decay, no release, no ceasefire, no tribute).
  At 90 and over, **kindred** ("wants to merge", "kin"); kindred both ways for 30 minutes, the weaker power joins the stronger: its ships change
  sides, its stock goes over, it is gone ("X joins Y").
- **New pacts:** trade agreement (both neutral or better, one greedy enough (0.5), both with an economy; each side earns 20 a minute; grows from
  non-aggression, and into an alliance), tribute (a much weaker power (under 0.6 of the other's might) at war, beaten past its pride line and not
  proud (under 0.6), pays a quarter of its income for peace, no end while paid; five minutes unpaid and it lapses, -20), vassalage (a tribute held an
  hour with the payer neutral or better: it pays, and fights its lord's enemies as an ally). The window names who pays whom.

### Where

| What | Where |
| --- | --- |
| The controllers: books, places, ships (hangar, building, out), missions (Convoy, Raid, Defend, Patrol, strays home), calls, merging, the section's bytes | `Source/Crew/CrewControllers.cs` (`PowerControllers`, `IPowerHost`) |
| Numbers and per-kind plans; a ship's bill from its blueprint's parts' Assembling recipes | `Crew/Controllers.json` (`call-of-slop-npc-controllers/1`), `Source/Crew/CrewControllerRules.cs`; a power file's `"plan"` overrides its kind's key by key |
| Bands (Kindred), blood feuds, the new events and pacts, `Gone`, `Might`, `Merge`, `Tell` | `Source/Crew/CrewRelations.cs`, `Crew/Relations.json` |
| The game's side: ships set down by the spawner (`RequestBlueprint(..., power)`, crewed by that power), docking, news, saves, on/off | `Source/Game/GameCrewPowers.cs`; `--powers on|off` (`Game.PowersForce`; default: survival worlds only) |
| Window | `UI/UiFactions.cs`: BLOOD/kin words, trade, tribute and vassalage lines, gone powers greyed |
| Tools | `--crew-check --crew-fleet powers [--seconds S]`, `--crew-fleet politics` (`Source/Crew/CrewPowersCheck.cs`); window verb `crewpowers [near]` |

A plan (in `Crew/Controllers.json` "kinds", or a power file's "plan"):

```json
"Traders": { "start": 4000, "income": 30, "ships": 8, "groups": 3, "places": 2,
             "fleet": { "cargo": ["IT Box Truck 1"], "escort": ["IT Technical 2"] },
             "missions": { "convoy": { "cargo": 1, "escorts": 2 } },
             "placeList": [ { "name": "Rime depot", "world": "Glacier", "at": [100, 0, -400] } ] }
```

Roles are cargo, escort, raider and warship; missions convoy, raid, defend and patrol. A kind with no plan has no economy (fields and trades
nothing). Bills today: IT Box Truck 1 516 units (109 parts), IT Technical 2 403, Bandit Buggy 1 373, Bandit Technical 1 371, British
Technical 2 393, British Gun Wagon 1 479.

### Saves and network

- Saves: a `CTRL` section (`WorldSaveWriter.ControllersTag`, version 1, after POWR): per power by name, its books, places, ships (by blueprint
  name; a ship out is a record that takes up the first crewed body of its power again, since body ids change), missions (group by slot and
  number) and the next convoy. A world from before has none: every power starts from its plan. No new save file. Fallen powers are re-marked in
  the relations on load.
- Protocol 354 (main 353 at the merge): `Msg.Factions` ends with u16 powers gone; pact kinds 4 to 6; -100 means a blood feud. Stocks, missions and ships are never sent.

### Tried by hand (2026-11-18, the 2-core box shared with other builders)

- `--crew-fleet powers --seconds 300` (about 70 s of wall time; a person at the middle, the file's numbers with waits and distances shortened): convoy formed with two
  escorts and delivered (three runs of five: in the others the raid pinned it); raid sent, engaged the convoy (looted in two runs of four: the convoy outruns it as often as not); call for help, the Militia
  answered and went; books equal stock at every tick, never below 0; 7 to 14 bodies set down, 3 to 7 docked; the section read back byte for byte.
  Thinks past the first minute: median 4 to 8 us, 99th 25 to 47 us; 4 us a tick on average in the tool (seven powers, each every second);
  88 bytes in all in thinks that said nothing (a ship set down or a buy logs, and allocates: a request).
- `--crew-fleet politics`: trade from non-aggression, a blood feud that nothing moves, tribute then vassalage, a merge, a save round trip: all OK.
- Window run (Glacier, survival, host and a joined client under xvfb, `crewpowers near`): the Traders bought a box truck and two technicals
  (1,322 of 4,000) and sent them under Admiral Rhea (the truck on Deliver, both escorts screening); the Raiders bought two raiders (744) and sent
  them (both on Engage, order Hold beside the convoy); the convoy called for help, the Militia answered, bought two warships (872), went with the
  first ready and escorted the convoy (Engage). The joined client's look lines named every group, admiral and captain; its news, grid and pacts
  matched the host's. Controllers 14 us a tick (the host at about a third of real speed on the loaded box); the section read back the same.
- Merged build (main 353): `powers` and `politics` OK again; thinks median 5 us, 99th 25 to 33 us. The worst single thinks (5 to 9 ms) came with
  the box's load at 15 to 20 (other builders' builds) and no log line in them; one, before the fix, was three ships docking in one think (a removal
  each): now one ship docks a think.
- Proving Ground is 384 m square: places shrink to its middle and nothing can be set down out of sight, so the controllers are for big worlds.

### Not verified

- A cargo ship's loot through a real raid in a running game (the tool's crews target by a stand-in for the game's fight rule); docking and the
  hangar in a game with a person near home; merging, tribute and vassalage between powers' ships in play (the engine only); a place lost and taken
  back in a game; a CTRL section through the real save file and a load (the bytes round trip in the tool and in `crewpowers`); a fallen power's
  greyed row (no picture).

### Questions (defaults taken; the user's or Karl's)

1. **Real production (answer 6).** Ships are bought from a stock at their blueprint's real bill (every part's recipe) and take its build time, at a
   power's home; no factory builds them yet. The factory generator (its prerequisite) is still being built and has no shipyard function. Default:
   keep the bill-from-stock until the generator can make a yard, then a power's homes become generated factory sites whose output feeds the hangar
   (what nobody sees estimated on the ledger, as linked logistics' closed worlds). Agree?
2. **On by default in survival.** Default: yes, survival worlds only (creative never; `--powers` overrides). A host setting in the lobby? Default: later.
3. **Places.** With no data and no outposts (phase 4), a power lays its places round the first person on each open world (1.2-2.2 km). Default:
   keep until phase 4's key sites; Karl's world sites replace them.
4. **Who may answer a call.** Default: allies always; neutral-or-better powers fighting the same enemy at loyalty 0.4 and up.
5. **Blood feuds with players.** Not built (only between powers); default: the same rule when phase 5 brings pacts with players.
6. **A fallen power's row** is kept, greyed, and never refilled; who takes a free row (an independent captain "making a name") is open.

### Left (later phases, or follow-ups)

- **The 1,000-body target** is phase 4's LOD (phase 2's measures stand: the world step of awake bodies is the limit). Today the world caps the
  controllers' bodies at 24 (`world.maxBodies`), spawning out of sight only.
- Space ships: the plans name ground vehicles; a power's space fleet waits for Karl's ship roles (the plans take any blueprint by role).
- Allies helping players (answer 9) is phase 1's crew rule (a power friendly to a person joins their fights it sees); powers answering a
  player's call and pacts with players are phase 5.
- Fog for players (answer 7) is the command view's FACTIONS layer (phase 4); the controllers' own knowledge is fogged already.
- Missions don't yet weigh their risk by the enemy's known strength beyond a raid's size check; abstract groups, presence and Take are phase 4.

### Review fixes (2026-11-18)

- A blood feud is a flag per pair (`PowerRelations.Feud`, POWR version 2 adds the bits), not the value -100: worlds from before (which clamped at
  -100) load with no feuds. Clients are shown -100 only for a feud (`Shown`: others at -99 at worst), so no wire change was needed for it.
- Trade, tribute, blood feuds and merging run only where the controllers do (`Might`/`Merge` set by `CrewPowersTick` while `PowersActive`): never in
  creative.
- A gone power (fallen or merged) crews nothing: `CrewWorld.LivePower` gives every new crew its heir (merged into), else another standing power of its
  kind, else none; at the tick a power goes (or is read gone from a save) every crew and group still of it is re-tagged (`Game.CrewPowersGone`).
  The heir is saved in CTRL.
- After a load, a ship out takes up only the body whose crew holds its saved uid (the captain's, else a member's), and that is in no group but its
  mission's; none: the record is dropped (logged). Two a think. Places are laid one a think; one ship docks a think.
- CTRL is written after FLET, and each power's missions find their groups again (slot and number, else the number) at its first think after a load;
  a mission whose group is gone sends its ships home quietly.
- Checks: `politics` adds -100 without a feud moving, and no trade or feud with the controllers off; `powers` adds a load (each ship out takes up
  its own body, or docked since). `--crew-relations` passed; `--selftest-only "Sandbox spawns, Save folder"` passed. Thinks past the first minute:
  median 5 us, 99th 137 us; the worst (4 ms) with the box at load 13 and nothing logged in that second (taken for preemption; not measured quiet:
  the box was never quiet).
