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
  group broke 0.7 s after and all three ran. `convoy` (four Raider ground vehicles as Militia, the leader the flagship): sent 300 m,
  followers 14 m off their places (as phase 2's check measures it), the leader there; a Zero Day 220 m off, the admiral pulled back.
  `mining` (three Traders Zero Days): mine, deliver, then back to mine. `interrogate`: the admiral gave up its group's two captains and
  the other admiral (three positions), a captain its ship and crew; resistance 0.76: 71 s with an interrogator of skill 200, 274 s alone.
- A window run (host and a joined client under xvfb, `crewconvoy Militia 3`, `crewfleet`): the spawner's convoy became group 1 under
  Admiral Vera on the leader, the other two joined with Form orders; the client's look lines read "Admiral Vera's flagship, Militia
  group 1, Captain Zed, crew of 5".
- Scale (`--crew-fleet scale`): 256 groups of 4 (1,024 bodies, 64 awake): the admirals 2.4 us a tick, worst 35 us, 0 bytes. 1,000
  Raider Helicopter 1 (23 parts) all awake and driven in 84 groups (driver cap raised to 1,000): admirals 34 us, crews 0.81 ms, the
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
             "fleet": { "cargo": ["4x4 Truck"], "escort": ["Hood Gun Buggy"] },
             "missions": { "convoy": { "cargo": 1, "escorts": 2 } },
             "placeList": [ { "name": "Rime depot", "world": "Glacier", "at": [100, 0, -400] } ] }
```

Roles are cargo, escort, raider and warship; missions convoy, raid, defend and patrol. A kind with no plan has no economy (fields and trades
nothing; since the generated faction vehicles went, 2026-11-21, the placeholders are the hand-built 4x4 Truck for cargo and the Hood Gun Buggy for every armed role). Bills then: IT Box Truck 1 516 units (109 parts), IT Technical 2 403, Raider Buggy 1 373, Raider Technical 1 371, Mercenary
Technical 2 393, Mercenary Gun Wagon 1 479.

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

## Phase 4: territory and the far layer (built 2026-11-18, branch `claude/faction-territory`)

Built to section 4 as Karl's answer 1 reworks it (majority control weighted by key sites' rarity), section 8, the user's answers 3 (space clock for
groups between planets), 4 (far fights by strength, a closed world never opened), 7 (fog) and 8 (1,000 bodies: real progress and a measure), Karl's 3
(the FACTIONS layer) and 4 (existing ships as placeholders).

### What a player sees

- **Planets held.** Frostreach's planets and moons have key sites (`Crew/Territory.json`: 31 spaceports, refineries, mines and outposts, each with a
  start holder). A site's control is its kind's weight shared among the sites of that kind on its planet (one spaceport there is worth a spaceport's
  whole weight; one of four mines a quarter of a mine's); a power with more than half a planet's control holds it, else it is contested. Held sites earn
  their power income a minute and keep a garrison (abstract: outposts as buildings with gun crews are later) that grows back while nobody attacks.
- **Wars over sites, seen or not.** Every few minutes a power at war with a site's holder sends a far Take group (its armed ships from the hangar, or
  bought at their bill) when it is strong enough by its caution; a power whose site is attacked sends a Defend group. Far groups move between planets
  on the space clock (distance at 3,000 m/s; 10 minutes when unknown), land and go to the site; there they fight by strength (1% of the enemy's strength
  a second, at most 2% of their own: minutes, never one roll), a ship whose hull runs out is lost (`destroyShip`); a group past its caution's line pulls
  back home. A site with no garrison and no defender left is taken after 2 minutes (`takeSite`), and a planet that changes holder is news ("Shard falls
  to the Raiders: the Traders have lost it", "X is contested") and a `takePlanet` event (-40). The attackers stay as its garrison for a while, then go
  home, where their ships go into the hangar. None of it opens a closed world.
- **Groups near a person.** A far group on the open world within 5 km of a person is set down as crewed bodies, at least 600 m from everyone, two a
  step, within the controllers' body cap (over it: it stays a record), then formed into a crew group under an admiral (phase 2) and sent to its site;
  with nobody within 7 km for a minute its ships go back to records one a step, hulls kept, and it goes on as a record.
- **Command view FACTIONS layer** (`docs/space/COMMAND.md` "The factions layer"): planets ringed by holder (dashed when contested) with tag and share;
  groups known (on this world within 8 km, or named by news in the last 5 minutes: dim, "N MIN AGO"), a guess at their task and ships, THREAT in red
  for a hostile power's strike or one within 3 km. The Faction window (F2) lists each planet's holder and share.

### Where

| What | Where |
| --- | --- |
| Sites, holders, garrisons, income, Take and Defend, far groups (move, fight, take, home), materialise and collapse, save bytes | `Source/Crew/CrewTerritory.cs` (`PowerControllers` partial, `FarGroup`) |
| Numbers and the planets' sites | `Crew/Territory.json` (`call-of-slop-npc-territory/1`; missing: no territory), `Source/Crew/CrewTerritoryRules.cs` |
| A ship's strength in a far fight | `ControllerRules.Bill.Strength` (1 + 2 a gun + parts / 60) |
| The host's side: the space clock, planets' distances, a site's place on the open world | `Source/Game/GameCrewPowers.cs` (`IPowerHost.SpaceSeconds/PlanetGap/SiteAt`) |
| Each player's map, fog, threats, `Msg.FactionMap` | `Source/Game/GameCrewTerritory.cs` |
| Command view layer, F2 planets | `Source/UI/UiCommandFactions.cs`, `UiFactions.DrawPowerRelations` |
| Far bodies' coarse rate | `Source/Constructs/ConstructFar.cs`, `ConstructPhysics.Pass`, `ConstructWorld.Step` (logic and power), `ConstructNet.PosePeriod` |
| Tools | `--crew-fleet territory`, `--crew-fleet far`, `--crew-fleet scale --fleet-spread M --far-every K` (`Source/Crew/CrewTerritoryCheck.cs`, `CrewFleetCheck.cs`); window verb `crewterritory [near|leave]` |

### The 1,000-body work (answer 8)

- **Far bodies' physics, logic and power** step once every 4 ticks by 4 ticks' time when an NPC body (not owned, held, carried, coupled or in space) is
  1.5 km or more from every person (`ConstructFar`): physics in a second pass over just those due this tick (a far body never meets a near one in the
  same pass); asleep far bodies' logic and power too. Sleep counts a far step as 4 ticks and lets a far body sleep at 4 times the still speed (at a
  coarse step a body at rest jitters at 0.2 m/s and never slept).
- **Network:** two more pose bands: past 1.5 km a body is sent every 32 pose ticks, past 4 km every 128 (the sleeping refresh tells a far body's last pose).
- **Measured** (`--crew-fleet scale --fleet-scale 1000 --fleet-awake 1000 --fleet-spread 8000`, Raider Helicopter 1, 23 parts, the 2-core box shared):
  the world's step 4.26 ms a tick before, 2.49 to 2.94 ms after (physics 1.71 to 0.75-0.82 ms, logic 1.75 to 0.79 ms); crews 0.20-0.27 ms; one client
  by the person gets 32 poses, 1.9 KB in 2.1 packets a pose tick with 427 movers. With all 1,000 within 1.1 km (phase 2's case) nothing changes: 8.6
  ms a tick, the near bodies' own cost.
- **What remains for 1,000:** near bodies (within 1.5 km) still cost full rate; machines' scans (0.55 ms for 1,000 bodies), the crews' 168 KB
  allocated in 240 ticks with bodies spread over 8 km (found before this branch: the crews' LOD moving; not looked into), batched drawing (not touched),
  and the controllers' body cap (`world.maxBodies` 24, spawning is slow: a vehicle a few hundred ms on this box). Big blueprints (Zero Day, 276 parts) were
  not measured again.

### Saves and network

- CTRL version 2: each power's flag for having held a site and its next Take; each ship's far group; then the sites (planet, name, holder by name,
  garrison) and the far groups (slot, power, task, state, real or not, planets and site by name, seconds to arrival, place, number, strength). A real
  group's ships are saved out with their crew's uid and taken up again by it after a load; a ship whose far group doesn't read goes into the hangar.
  Version 1 still reads (the territory starts from the data). No new save file.
- Protocol 356 (main 355 at the merge): `Msg.FactionMap`, once a second to each player. `enum Msg` is full: it takes `RemovedTutorial`'s slot.

### Tried by hand (2026-11-18, the box shared with other builders)

- `--crew-fleet territory --seconds 400` (16 power files written for the run, 5 planets of 8 sites, nobody anywhere, times shortened): 22 to 32 sites
  taken, 6 to 16 planet changes, each in the news; far groups topped up to 64-73 after two minutes: controllers and far layer 12 to 18 us a tick
  (99th 44-51 us, worst 0.1-1.2 ms), 0 bytes in steps that said nothing; the section read back the same.
- `--crew-fleet far`: nothing set down while the person was 9 km off; within 3 km both the Raiders' Take group and the Militia's Defend group
  materialised (the nearest body 3 km from the person), collapsed 10 s (the tool's time) after the person went 10 km off, hulls kept.
- Phase 3's `powers`, `politics` and `--crew-relations`: OK (looted no in that run: the convoy outran the raid, as noted before).
- Window run (Glacier, survival, host and a joined client under xvfb): nine planets with holders on host and client; `crewterritory near` put a
  Militia Take group 3.5 km off, it materialised as a crew group (the client's look lines named its admiral and captains), the client's map knew it
  ("here, real"); `crewterritory leave` (ranges shrunk) collapsed it, the client then knew it unreal. Far layer 11 us a tick.

### Not verified

- The FACTIONS layer as drawn (no picture taken); the F2 planets list as drawn.
- A real save and load with a far group real (the uid adoption) or in transit; a save from before (version 1) read through the game.
- A real Take by bodies (garrison worn down by a real group, site taken) in a running game; a group set down on a planetoid at a site given by
  latitude and longitude (the window run put it by the player); groups in transit between planets on a real space clock.
- Far bodies' coarse rate with wheeled vehicles on slopes and with big ships (only helicopters at rest and flying were measured).
- The 7 ms worst far-layer tick seen once in the window run (before any group was near; taken for the loaded box, not traced).

### Questions (defaults taken)

1. **Players holding planets** (Karl's 1 says player factions hold them the same way): no player site exists yet. Default: later, with player-built
   outposts counted as sites by kind.
2. **Outposts as buildings.** Garrisons are numbers. Default: the factory and base generators' sites become the key sites' buildings, their gun crews
   the garrison, in a later phase.
3. **Far groups on other open places** (planetoids run as a place on the host): the far layer sets bodies down only on the host's main world. Default:
   a place's Game asks the main Game's controllers (one layer), later.
4. **Body cap.** Kept at 24 (`world.maxBodies`). Default: raise it once spawning is cheaper; the LOD above is what lets it go up.
5. **Allies' sightings for fog.** Not shared yet (no pacts with players until phase 5). Default: an ally's groups and what its crews see, then.

## Phase 5: players in the politics (built 2026-11-18, branch `claude/faction-players`)

Built to section 10's phase 5 row, section 5.5 ("with players, pacts are proposed and answered through the Faction window"), section 7, the user's answer 9
(allies help players: join fights near them, answer their calls) and Karl's answer 2 (phase 1's model and more pacts), with phase 3's question 5 (blood feuds
with players) and phase 4's question 5 (an ally's groups known) taken up.

### What a player sees

- **Pacts (Faction window, F2, "Pacts with the world's powers").** Each power's row says the pact with your faction (minutes held or left) or none, its
  standing, a proposal waiting, or BLOOD FEUD. The **leader** gets buttons for the pacts its standing allows: Ceasefire and Pay tribute below 0, Non-aggression
  and Trade from neutral (20), Alliance from friendly (60); and Break pact. The power answers at its next think (a few seconds) by its own scoring, as it
  answers another power: a ceasefire from anyone it fights unless it is aggressive (0.8) and hasn't lost enough to the faction (its pride line, phase 1's);
  tribute when greedy enough; non-aggression and trade from neutral (trade only for a greedy power); an alliance when friendly, loyal and with an enemy in
  common. Turned down, the notice says why, and the leader asks again after 5 minutes. A power offers a pact by itself when it wants one (a ceasefire when
  it is losing, an alliance, trade or non-aggression as between powers): "Militia offers you a ceasefire", Accept or Decline, for 10 minutes. Members who
  aren't leaders see the pacts and are told only the leader can act.
- **What a pact does.** Its crews hold fire on the faction's people and their crewed ships, and the faction's hired crews on theirs, whatever the standing
  (one who shoots is a foe again). A ceasefire lasts 30 minutes and lifts the standing to -40; a tribute costs the leader 150 credits every 10 minutes
  (unpaid: it lapses, -20); a trade pact pays the leader 10 credits a minute (and the power's stock 20). Alliances and trade fall apart below 15,
  non-aggression at war. An alliance: the power's crews fight anyone they see fighting the faction's people (phase 1's help rule), its groups are known to
  the faction on the command view's FACTIONS layer (fog, phase 4), and it answers the faction's call.
- **Breaking.** The leader's button, or a member's attack (8 stray hits a minute forgiven, as between powers; past that, deliberate). News for everyone
  ("Client Co breaks its ceasefire with Militia"); the power takes `breakPact` (-40); every power that knows (in a pact with the victim or friendly to it, in
  a pact with the faction, or with a crew that sees the leader) takes `breakPactHeard` (-15 by loyalty); the faction is told who heard, and the player
  factions allied with it are told too.
- **Call allies** (any member, once every 3 minutes a faction): the allied powers answer (Defend: warships go to the caller and follow them while the danger
  lasts); a power neutral or better that fights the caller's foe answers at loyalty 0.4, as a power's call. Only where the controllers run (survival); in
  creative the button says so (allies' crews nearby still fight).
- **Blood feuds with factions:** a grave deed (a ship destroyed or taken, an officer captured, a convoy raided, a planet taken) against a power already at -99
  by a faction's member, its pride 0.4 or more, where the controllers run: -100 for good, no pact, no contract, no decay. A person in no faction swears none
  (their standing stops at -99).
- **Contracts for credits ("Contracts for credits", seen by everyone, a lone player too).** Each power with a mind offers one in turn every 45 s (two open
  at most each, 24 in the world, 15 minutes to take): escort one of its crewed ships (a convoy's flagship first) for 3 minutes within 400 m (300 credits, 20
  minutes), deliver 40-160 units of a material (Relations.json's list) to its place in a ship within 60 m (100 + 2 a unit, 30 minutes), raid a power it
  fights by destroying or taking one of its ships (400, 30 minutes). Taken by one whose standing with it is 0 or more, or whose faction holds a pact with it;
  two at once; a raid on a power the faction holds a pact with is refused ("break it first"). Tracked once a second; done: paid and `contractDone` (+5);
  failed (out of time, the ship lost, given up): `contractFailed` (-5). The taker's line shows how far along and how far away. Creative pays as survival does
  (unlimited credits shown).

### Where

| What | Where |
| --- | --- |
| Pacts with factions, proposals, offers, breaking and who hears, feuds, wars' losses, notes for members, save bytes | `Source/Crew/CrewPartyPacts.cs` (`PowerRelations` partial); hooks in `CrewRelations.cs` (`Post`'s player path, `MovePlayer`, `Think`, `Decay`, `Fall`, save version 3) |
| Contracts: offers, take, drop, work, raid deeds, save bytes | `Source/Crew/CrewContracts.cs` (`PowerContracts`, `IContractHost`), held by the relations (`PowerRelations.Contracts`) |
| Numbers | `Crew/Relations.json`: events `contractDone`, `contractFailed`; blocks `players` and `contracts` |
| The game's side: the window's acts, notices, tribute and trade credits, the contracts' host, `Msg.Contracts` | `Source/Game/GameCrewPolitics.cs`; fight rule in `GameCrew.cs` (`Fights`, own crews, `CrewHelpsAgainst`), `PowerRelations.ShipFights` |
| A faction's call | `PowerControllers.CallForParty` / `HearParty` (`CrewControllers.cs`); `IPowerHost.PersonAt` (a Defend follows the caller) |
| Window | `UI/UiFactionsPolitics.cs` (`DrawPartyPacts`, `DrawContracts`), from `UiFactions.cs` |
| Tools | `--crew-check --crew-fleet players` (`Source/Crew/CrewPlayersCheck.cs`); window verb `crewpolitics [found NAME|invite|acceptinvite|standing POWER V|pact POWER KIND|accept POWER|break POWER|call|take N|drop N]` |

### Saves and network

- Saves: the `POWR` section's version 3 adds, after the feud bits, the pacts with factions (kind, power by index, faction id, seconds held and left,
  hours kept, the next tribute), the factions' feuds, and the open and taken contracts (taker by name: taken up again when they come back; an escort's ship
  by its captain's uid). Versions 1 and 2 read (no pacts with factions, no contracts). Faction ids are the world's own; a faction that ends loses its pacts,
  feuds and standing at the powers' next thinks, so a new one in its id starts clean. No new save file. A Defend for a faction's call isn't kept as one: after
  a load it holds where it was called.
- Protocol 359 (main 358 at the merge): `Msg.Factions` ends with the pacts with factions, the powers' offers, the factions' proposals and the feuds with
  factions; `FactionOp` gains Pact, PactAnswer, PactBreak, CallAllies, ContractTake and ContractDrop at its end; `Msg.Contracts` (all contracts, on change,
  at most once a second, and to each joiner) in the slot of `RemovedColonyAlarm`.

### Tried by hand (2026-11-18, the 2-core box shared)

- `--crew-fleet players`: all OK: Traders and Militia allied; a faction at war with the Militia (-70) proposes a ceasefire, signed (-40, fire held); the
  leader breaks it: news, the Militia -40 -> -80, its ally the Traders hear (30 -> 15; Salvagers too, friendly to the Militia); 8 stray hits forgiven, the
  ninth breaks a second ceasefire; the Militia, beaten, offers a ceasefire and it is taken; an alliance with the Traders makes their crews fight the
  Mercenaries fighting the faction's person, not otherwise; a tribute unpaid lapses; an unwanted alliance turned down and the ask held off; a blood feud
  (Might set) refuses a ceasefire; contracts: escort, delivery and raid offered, a raid on a pact partner refused, a third refused, escort and delivery done
  and paid (640), a raid done by a destroyed ship, an escort failed when its ship was lost; a save round trip keeps the pacts and a taken contract, the
  feud too; a version 2 section reads. Relations 0.05 us a tick with 0 bytes allocated in 3,600 ticks; contracts 0.56 us a tick, nothing allocated
  but an offer's or an end's line.
- `--crew-fleet politics` and `--crew-relations` (with its host and joined client): OK, as before.
- Window run (Glacier, survival, host and a joined client under xvfb, `crewpolitics`): the client founded Client Co and invited the host, who joined; the
  host (a member, not the leader) asking for a pact was told "only your faction's leader can make or break pacts"; with the Militia at -70 the client leader
  proposed a ceasefire and the Militia signed ("Client Co and Militia sign a ceasefire"), both peers' tables showing it; the client broke it: news, Militia
  -40 -> -80, "Word gets around: Raiders, Salvagers, Traders think less of you", the Traders 70 -> 55 on both peers; the call went out (nobody answered: no
  ally, the Militia now at war, the Traders no Defend plan); a contract from the Militia refused at -80. A second host run took a delivery contract, saved
  the world through the real save (`saveworld`) and a third loaded it (`loadworld`): the non-aggression pact and the taken contract (its taker found again
  by name) came back.

### Not verified

- A contract done in a running game (an escort by a real convoy, a delivery out of a real ship's bins, a raid by a real kill), the escort's 400 m measured
  against a moving ship; a tribute or trade paid in credits in a game; an allied power's Defend group reaching and following a player; allied crews fighting
  a faction's foes in a real fight (the engine's rule is checked, not bodies shooting); the window's buttons as drawn (no picture: driven by the verb);
  the FACTIONS layer showing an ally's groups.

### Questions (defaults taken)

1. **Who may act.** Pacts: the leader only (the task's rule; the rank permission `FactionMay.Relations` is not used for them). Call allies: any member.
   Contracts: anyone, a lone player too; pay to the taker. Agree?
2. **Pacts open to factions:** ceasefire, non-aggression, trade, alliance and a tribute the faction pays. Vassalage (a faction serving a power, or a power
   a faction) and a power paying a faction tribute are not offered. Default: later, if wanted.
3. **Credits and units** are one for one where they meet (a faction's tribute and a trade pact into a power's stock).
4. **A lone player** (no faction) makes no pacts and swears no feuds; their standing stops at -99.
5. **Hearers of a broken pact** include powers in a pact with the breaker (they learn its word is worth little) and any that see it, even the victim's
   enemies. Default kept; say if enemies of the victim should cheer instead (phase 1's +1/5 rule for deeds).
6. **Contract kinds:** escort a crewed ship (no convoy needed, so creative has them), deliver to the power's place (in creative, or a power with no place
   here: a meeting point 800 m from the taker), raid. Default: more kinds (salvage, bounty on a named captain) later.

### Left

- Player factions holding planets (phase 4's question 1) and players as admirals (phase 6).
- A power's view of a faction's allies (a faction allied with a power's enemy is not yet thought less of).

### Review fixes (2026-11-18)

- Only a hit (`hitBody`, `hitCrew`) by a member on a pact partner may be stray (8 a minute); a kill, a ship sunk, a boarding or a capture breaks the pact at
  once and counts in full. A forgiven hit is no deed (`PowerRelations.Post` returns true) and the game clears the foe mark the hit made
  (`CrewShip.UnmarkFoe`), so an allied crew clipped by splash doesn't turn on the member.
- A taken contract follows its taker by name: when they leave (their id goes to the next joiner) it is held for them, unworked and unpaid, its clock
  stopped, and taken up again when someone of that name comes back; pay goes only to the taker present (`PowerContracts.Present`).
- `Msg.Contracts`' read guard is 33 bytes an entry. Checks: `players` adds splash on a partner (forgiven, the ceasefire holds), a kill under a ceasefire
  (breaks it) and the taker away (held); `--selftest-only "Packets, Save folder, Frame allocations, Interaction sweep"`, `--crew-relations`, `politics`
  and `players`: all passed after merging main (protocol 359).

## Phase 6: player admirals (built 2026-11-18, branch `claude/faction-player-admirals`)

Built to section 10's phase 6 row, section 7 ("players as admirals"), the user's answers on admirals (a role in the hierarchy, combat and civilian; the
fleet command network; doctrine; 256 groups) and Karl's answer 3 (fleet orders in his command view, beside the FACTIONS layer).

### What a player sees

- **Fleet groups (command view, bottom left: `docs/space/COMMAND.md` "Your fleet groups").** A ship of yours with a hired crew starts a group, or joins
  the picked one; "crews near me" takes every one within 3 km (12 ships a group, 16 groups a player, the world's 256 shared with the powers). You are
  its admiral, or one of your hands (job Admiral) aboard a member: a hand keeps the group going while you are away, and your orders reach it only over
  the net. Your faction's leader may command your groups too (owner or leader: checked on the host).
- **Orders** (the panel and the right-click menu): Hold, Form on me (the body you are aboard; on foot, come to me), Go here, Engage (the whole group on
  one target), Escort (every ship a slot on what it guards, its gunners firing from there, and in to fight what comes within 1 km of it), Screen,
  Close to board, Pull back (to a point, or home); a civilian route of up to 8 stops (load, unload, mine, deliver, pass through; each with a work time;
  looped); formation (column, line, wedge, screen) and spacing; doctrine (any of Crew/Fleet.json's: caution, discipline, skill, succession); task (a
  civilian task: warships screen the flagship and it keeps going). A player admiral's group never pulls back by itself (you decide); a hand admiral
  weighs strength as the powers' admirals do. Succession by doctrine (default "Player Crew": captain, 6 s); with nobody left to lead, you lead it.
- **The fleet net carries orders:** aboard a ship with a radio part you reach 25 km, else your hand set 1.5 km; ships pass it on to each other. Out of
  reach, the act is refused ("no ship of group 2 hears you: out of reach of the fleet net ...") and the panel says OUT OF REACH.
- **Hired hands take the helm only in a group** (they never did before: "never the helm unless a player orders it"); out of one, the helm hand stands
  down. Your own seat at the helm is yours: they keep the guns. A nav order (Karl's orders) given to a group's ship takes it out of the group; a group
  order ends its ships' nav orders. A ship given away leaves its group.

### Phase 2's leftovers fixed (they blocked the done-when)

- **Loose air formations, frigates settling to the ground:** three causes. (1) A Zero Day's docked Limpet (a body it carries) was read as the ground 4 m
  under it, so every frigate under orders climbed for ever (up to 200 m over its slot): clearance rays skip what the body carries. (2) Under orders and in
  the fight's strafe a ship-pattern craft (FlightPattern.Ship, the Zero Day) slid on Q and E, which roll it; it rolled onto its side, lost lift and
  settled for good: slide keys come from the flight pattern (A and D for a ship), and its pilot holds it level with Q and E (`ShipAi.Level`). (3) Ships
  crossed into each other when their anchor turned: slots are spaced by the biggest member (twice its size plus 25 m) and stepped up by turns in the air,
  a ship climbs over its anchor, and a ship ahead in the formation no longer reads as an obstacle to climb over.
- **Escorts can't keep up:** a ship's top speed is learnt as it flies full ahead under orders; an escort asks what it guards (any crewed ship) to fly at
  most 0.7 of its slowest ship's.
- **Friendly fire:** a ship under Form, Escort or Screen holds fire (guns and racks) while its ward is across the line (the base's screen check lost five
  crew a run to it: now none).
- Helicopters under the crews' flying: not looked into (the checks use Zero Days, as before).

### Where

| What | Where |
| --- | --- |
| Players' groups: form, join, leave, admiral, flagship, route stops, the commander's reach on the net, orders into the admiral code | `Source/Crew/CrewFleetPlayers.cs`; `Think` in `CrewFleet.cs` (the player's node on the net, guard and Engage orders, spacing, speed cap) |
| Formation flying fixes | `Source/Crew/CrewOrders.cs`, `Source/Constructs/ConstructShipAi.cs` (`Clearance`, `Level`, slide keys, `WardInLine`) |
| Own hands at the helm in a group | `CrewWorld.Fill`, `ShipAi.Scan` (`CrewAims`) |
| The host's side: acts, owner and leader checks, the net check, commanders once a second, `Msg.FleetGroups`, nav orders | `Source/Game/GameCrewFleetPlayers.cs`; `NavOrderAct` (GameNav.cs) |
| The panel and the menu's group orders | `Source/UI/UiCommandAdmiral.cs`; hooks in `UiCommand.cs`, `UiCommandOrders.cs` |
| Tools | `--crew-check --crew-fleet escort [--seconds S] [--no-raider] [--escort-ship NAME] [--miner NAME] [--every S]` (`Source/Crew/CrewAdmiralsCheck.cs`); window verb `crewadmiral ships N BP / LEAD|lead|new [all|lead]|use N|add|order KIND [ID|lead|crewed|ride]|formation|spacing|doctrine|task|admiral hand|me|stop WORK X Z S|disband|show` |

### Saves and network

- Saves: the `FLET` section's version 2 ends each group with its owner's name, the player-admiral flag, its shape and spacing (and the flagship's id as
  a hint). Version 1 reads. A player's group with an Escort, Form, Screen, Engage or Board order comes back holding (bodies' ids change). The owner is
  found again by name when they come back. No new save file.
- Protocol 368 (main 367 at the merge): `CActKind.Fleet` at the end of CActKind (u8 FleetOp, u8 slot, u16 body, u32 uid, vec, u8 arg, f32 value);
  `Msg.FleetGroups` (once a second, only when it changed, to each player for the groups they may command; no strings) in RemovedObjective's slot.

### Tried by hand (2026-11-18, the 2-core box shared, load 7 to 10)

- `--crew-fleet escort` (three hired Zero Days escort the player's hired Magpie Mining Ship on a mine-and-deliver route; a Raiders frigate comes):
  formed on the miner at rest 17-25 m off their slots; on the way median 38 m, 90th 111 m (past 100 m only while a turn swings the slots round);
  the miner flew its route at 18 m/s (capped by its escort); with the raider within 1 km all three engaged and took it to 36%; the miner lost its
  cockpit (the raider fired on it from 760 m first). Admirals 1-2 us a tick, 0 bytes.
- Phase 2's `strike`, `succession`, `convoy`, `mining` (now loops twice: it no longer climbs), `interrogate`, `screen` (8 of 8): OK after the fixes.
  `--selftest-only "Packets, Save folder, Sandbox spawns"`: passed.
- Window runs (Glacier, survival, host and joined client under xvfb, `crewadmiral`): three Zero Days and a Magpie set down with hired crews; group 1
  escorting the Magpie, group 3 the Magpie on a two-stop route: all on the net, hands at the helm, the Magpie mining, the escorts 80-140 m off their
  slots on the host's slow frames; the client, leader of the host's faction, saw both groups and changed the shape and the order (the host accepted
  them); the look lines read "Player Crew group 1, crew of 7". The world saved (`saveworld`) and loaded in a third run: both groups and the route back,
  the escort holding; an order after the load reached them. The panel drew in the command view (own check).

### Not verified

- The panel's buttons and the right-click group orders clicked by hand (driven through the act by the verb; the menu was not opened).
- A hand as admiral in play (the act runs; nobody watched it lead while the player was away); a cut-off net in play; Board and Screen by players'
  groups; ground vehicles in players' groups; helicopters.
- The worst admiral tick on a loaded box was 3-4 ms once in two checks (load average 10; 0.2-0.4 ms on a quieter box).

### Questions (defaults taken)

1. **Who may command a player's group:** its owner and their faction's leader. Members of the faction? Default: no.
2. **Hired hands at the helm:** only in a group (the old rule, "never the helm", kept outside one). Default kept.
3. **A player admiral never pulls back by itself;** a hand admiral weighs strength like the powers'. Default kept.
4. **Escort band** 1 km from the ward and the escort's speed cap at 0.7 of its slowest ship: numbers in code (`CrewFleet.EscortBand`), not data yet.

### Review fixes (2026-11-18)

- A ward slows only while an escort is falling behind its slot (30 m behind along the ward's way), to 0.8 of that escort's top speed, and the ask
  lapses 3 s after; the top speed is a running mean of its full-ahead speed, rising and falling with its load and damage (it applies to the powers'
  Defend escorts too). `escort`: median 39 m, 90th 115 m.
- Players' groups are keyed on the owner's saved identity (world key, else verified Steam id), not the name (unique only among those online): a
  namesake of an away owner can't command or stand them down, and a rename keeps them. FLET version 3 adds the key; a version 2 group is claimed once by
  its owner's name. A ship is taken out of a group only when another player present owns it.
- A power's escort group fights what it sees, as in phase 2 (Fight on, no band); a player's escort keeps its slot and goes in for threats within
  1.8 km of its ward.
- In a formation the look-ahead skips only the ward and the group's own ships: the ground, bases, stations and anyone else's ships ahead make it climb.
- `--selftest-only "Packets, Frame allocations, Save folder"`, `--crew-fleet all`, `escort`, `powers`: OK.
