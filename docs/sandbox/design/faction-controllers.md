# Faction controllers and admirals: the two top tiers of the bot brain (design)

Design only (2026-11-17). No code changed, nothing built, nothing run. Written for builders who each take a whole phase
(section 11), a WIP commit after each step. Section 12 holds the open questions for the user (recommendation first),
section 13 what must be agreed with Karl. An answer there overrides the text above it.

The user's idea (2026-11-17): the bot brain gets four tiers: **crew** (built), **captains** (built, one per ship),
**admirals** (new) and a **faction controller** per faction (new). Each faction may have different operating parameters,
and factions may be in conflict or allied with each other in many ways that change through play.

Sources, read in excerpts: `docs/sandbox/design/bot-brain.md` and its notes; `Source/Crew/CrewFactions.cs`,
`CrewWorld.cs`, `CrewGround.cs`; `Source/Game/GameCrew.cs`, `GameCrewFactions.cs`, `GameCrewBoard.cs`;
`Source/Constructs/Factions.cs` (`FactionTable`); `docs/sandbox/SURVIVAL.md`; `docs/sandbox/design/linked-logistics.md`
(the ledger, closed worlds); `docs/space/COMMAND.md`; `docs/vehiclekit/ROLES.md` (roles, families, convoys); the archived
story engine (`docs/archive/story/STORY.md`) for ideas only.

Not verified, for phase 0 of the first builder: how `Msg.Factions` lays out the NPC rows today (section 9 adds a 16 x 16
block to it); whether a faction's ship can live in `SpaceShips` while no bubble holds it (as a player's ship on a trip
does); what a generated vehicle costs to spawn on the 2-core box (a convoy of 4 took 7.2 s: section 8 plans around it);
what holds a planetoid's Game open (`OpenPlace`) and whether the host's main Game can see a closed world's faction bodies.

## 0. What is there today, and the gap

| Tier | Today | Where |
| --- | --- | --- |
| Crew | Records in a struct of arrays (thousands), seats and stations, LOD tiers (bodied, seated, asleep), jobs, boarding, scuttle | `CrewWorld`, `GameCrew*.cs` |
| Captain | One per crewed body, no body: reads hits, hull and losses into morale, picks Hold, Engage or Flee; boarding acts | `CrewWorld.Captain`, `GameCrewBoard.cs` |
| Convoy | A leader drives out and back; followers keep their slot (`ConvoyIndex`, `Lane`) in the leader's frame | `CrewGround.cs`, `VehicleConvoys` |
| Faction rules | `Crew/Factions/*.json`, one file per **kind** (Traders, Mercenaries, Raiders, Militia, Salvagers, Player Crew, Test Hostile); stance to players and to each other kind, **fixed at load** (`NpcFactionDef.ToFactions`) | `CrewFactions.cs` |
| Standing | Each kind's standing with each player faction (`StoryStanding`, 32 x 16) and lone player (`PlayerStanding`, 256 x 16), -100 to 100, saved, sent by `Msg.Factions`; a deed moves it, below 0 the kind's crews fight; a hit also raises the victim's enemies' view a little (`Deed`: a fifth) | `GameCrewFactions.cs` |
| Player factions | 32 factions (F2), a stance each way (war, neutral, wants alliance); allied when both want it, at war when either says so | `FactionTable.Stance`, `Relation` |
| Call for help | Once per boarding, the same kind's awake crews within 3 km turn on the boarder | `GameCrewBoard.CrewCallHelp` |

The gap: nobody decides **why** a ship is where it is. Ships appear from the Spawn tab or the vehicle spawner, fight what
is near, and that is all. Kinds never change how they see each other, nothing holds a planet, and no ship works with
another except a convoy's followers. This design adds the two tiers that give ships a reason.

## 1. The four tiers

| Tier | Decides | Scope | Thinks | Orders go to | Reports go to |
| --- | --- | --- | --- | --- | --- |
| **Faction controller** | Strategy and relations: who is a friend, who is an enemy, which planets to hold or take, what to spend, which missions to run | One faction, the whole system | Every 5 s (real time), one controller a tick round robin | Admirals, as **missions** | (the world: news lines, the Faction window) |
| **Admiral** | Operations for one group: route, formation, which contact to fight, who escorts whom, when to retreat, when to ask for more | One group (2 to 12 ships) | 1 s with a player near, 5 s far; abstract groups at the controller's rate | Captains, as **orders** | Its controller, as **reports** |
| **Captain** (built) | Tactics for one body: engage, hold, flee, boarding acts, which stations to man | One crewed body | 1 s bodied, 5 s far (as now) | Crew, as a **station plan** | Its admiral |
| **Crew** (built) | Work at a station: drive, lay a gun, mend, load, walk | One member | Per tick by its driver (as now) | (keys and acts, as a player's) | Its captain (as now: losses, state) |

**The rule between tiers: a tier only narrows what the tier below may do; it never presses keys.** A mission says "defend
Rime"; the admiral turns that into "hold station over the north outpost in a line, fight anything hostile inside 4 km";
the captain still picks Engage or Flee for its own body inside that, and its crew still need to be in their seats (rule 1
of bot-brain.md is untouched). So a controller or an admiral that breaks costs strategy, never a ship that flies with an
empty helm.

**Orders down** are small structs written into the tier below (no messages, no allocation): a mission into an admiral's
record, an order into a `CrewShip`. **Reports up** are events pushed into a fixed ring per group (64) and per faction (64)
and read at the reader's next think; a full ring drops the oldest and counts the drop (logged once a minute, never silent).

## 2. Factions: kinds and the factions made from them

Today a "faction" is a kind: every Militia ship in the world is one Militia. The user's idea needs factions that differ
and fall out with each other, and SURVIVAL.md's decided rule is "NPC factions are kinds". Both fit if a **faction** is an
actor made from a **kind**:

- `Crew/Factions/<Name>.json` stays the kind (rules of the crew: engage, morale, boarding, scuttle, hiring). Unchanged.
- A new `Crew/Powers/<Name>.json` (`"format": "call-of-slop-npc-power/1"`) is one faction: `"kind": "Militia"`, a name
  ("Rime Home Guard"), a colour and tag, its operating parameters (section 3), its starting relations (section 5), its
  starting holdings and fleet (section 4). Two Militia powers can be at war with each other; a Raider power can buy
  peace with one Traders power and not another.
- **With no Powers folder, each kind is its own power** (one power per kind file, kind's name), so today's worlds and
  `--crew-check` behave exactly as now.
- **Rows.** Powers take the factions table's NPC rows (`FactionTable.MaxStory`, 16): `StoryNames` names powers, not
  kinds, and standing is per power. `CrewShip.Faction` and `Construct.NpcFaction` become the power's id; the kind is read
  through it (`CrewPowers.Of(id).Kind`). Saves keep names, as now (`CrewFactions.Find`), so renumbering is safe.
- 16 powers is ample for one system of five planets (question 1). Player Crew and Test Hostile stay kinds with no
  controller (a power with `"controller": false`): their ships behave as today.

## 3. The faction controller

One class (`FactionController`, `Source/Crew/Factions/` or `Source/Crew/CrewPowers*.cs`; `Game` side in
`Game/GameCrewPowers*.cs`), one instance per power, the same code for all. Server only, in the host's main Game (it always
runs and holds space, as the link ledger does).

### 3.1 Operating parameters (data, per power)

| Key | Range | What it changes |
| --- | --- | --- |
| `aggression` | 0-1 | Weight of Raid and Take missions over Patrol and Defend; how low a relation must fall before war is declared |
| `greed` | 0-1 | How much a planet's resources and a convoy's cargo weigh as targets; willingness to break a pact for gain |
| `loyalty` | 0-1 | Share of an ally's grievance it takes on (third-party events); how likely it answers Call allies; how rarely it breaks a pact |
| `forgiveness` | 0-1 | How fast its relations drift back to their rest values (section 5.4) |
| `caution` | 0-1 | Strength ratio it wants before attacking (1.2 cautious ... 0.8 reckless); the admirals' retreat line |
| `pride` | 0-1 | How much a loss to someone hurts the relation beyond the loss itself; how hard it is to make peace after defeat |
| `values` | weights | What it cares for: `planets`, `cargo`, `ships`, `crew`, `credits`, `reputation`: the controller's mission scoring and the size of relation events (a Traders power values cargo, so a raided convoy hurts more) |
| `needs`, `wants` | resource lists | (the kind's existing keys, now read) What it must have (fuel, metal: shortage drives Convoy and Take missions) and would like (trade, later) |
| `doctrine` | name | Its admirals' doctrine (section 6.4) |
| `budget` | numbers | Start stock, income per held planet, ship costs by class, the most ships and groups it may field |

The same code with different numbers gives the feel: a Raiders power (aggression 0.8, greed 0.9, loyalty 0.2, forgiveness
0.1) raids convoys and breaks deals; a Traders power (aggression 0.1, values cargo, caution 1.4) runs convoys with
escorts, pays for peace and flees; a Militia power (values planets 1.0, loyalty 0.8) defends what it holds and answers
its allies.

### 3.2 What a power has

- **Assets**: its ships (a record each: model or blueprint key, class, role, crew count, hull share, cargo; whether it is
  a real body now: section 8), its groups and admirals, its outposts (bases on planets: their guns are stations with crew,
  as bot-brain.md section 10), and a **stock** of abstract resources and credits.
- **Territory**: the planets (and later moons and asteroid fields) it **holds** (section 4), each with a presence value
  (its defence force and outposts there).
- **Relations**: its row of the relations table (section 5) and its pacts.
- **Missions**: a fixed list (at most 16 a power), each with a type, a target (a planet, a place, a route, a group, a
  power), a group assigned, a priority and a deadline.

### 3.3 Missions

| Mission | Given when | The admiral does |
| --- | --- | --- |
| **Patrol** | Always, for each held planet, scaled by its value and the threats seen there | Flies a loop over the planet's routes and outposts; engages hostiles under its strength, reports the rest |
| **Defend** | A held planet or outpost reports an attack, or an enemy group is seen heading for it | Goes there, holds station, fights; calls for reinforcement when outmatched |
| **Convoy** | A need is short at one holding and stocked at another (or a trade run, later) | Moves cargo ships from A to B in formation with escorts; the cargo moves the stock |
| **Escort** | A convoy or a high-value ship has a route through danger | Keeps station on the escorted group, engages what comes near it |
| **Raid** | Aggression and greed against a power (or player faction) it is hostile to: its convoys, routes, outposts | Hunts the target's ships or route, strikes, takes loot (salvage, cargo), leaves before the defence comes |
| **Take** | At war, the target planet's presence is under its strength by its caution | Destroys or drives off the holder's defence force and outposts there, then sits there to hold it |
| **Retreat** | Losses past the doctrine's line, or a pact signed | Breaks contact and goes home |
| **Call allies** | A war it is losing, or a defend it can't meet | Sends a request to each power it holds an alliance with (section 5.5); they answer with a mission of their own |

Scoring: each candidate mission's utility is `value of the target (by values) x need or threat x (1 - risk by caution)`,
weighed by aggression or loyalty as the table says, minus the cost of the ships it ties up. The controller keeps
missions it has running unless one falls under half the best new one (hysteresis, so groups don't flip back and forth).
Bounded: candidates come from its holdings (5 planets), its enemies' known holdings and routes (at most 16), and its
allies' calls: at most a hundred scores a think, no allocation.

### 3.4 Ships within a budget

A power fields ships from its **stock**: each hull class has a cost (data); income comes from held planets each real
minute; losses are not refunded. Caps: at most `ships` (data, default 24) and `groups` (default 6) a power, and the
world's caps in section 10. New ships appear at a **home** (a held planet's outpost or a station named in the power file),
never in sight of a player (300 m from every person and out of their line of sight, or simply on a closed world).

What a ship is comes from the vehicle kit and Karl's ships: a role (scout, raider, gun platform, carrier, tanker, recovery,
command, boss: ROLES.md) and a tier, in the power's style (`styles` of its kind), as a **model key**
(`VehicleModel.Key`): the same key always makes the same vehicle, so a ship record needs only its key to be made again.
Space ships come from a list of blueprints per role in the power file (Karl's frigates and miners; no generator for
space hulls exists yet: section 13). A family's marks give a power's fleet a recognisable line (Mk1 to Mk3 as it grows).

## 4. Territory: who holds a planet

SURVIVAL.md (decided): factions hold resources by holding the planets they are found on. This design proposes how a
power holds one (to agree with Karl, section 13):

- A planet's **holder** is the power with the most **presence** there, if that is at least 60% of all presence there;
  otherwise it is **contested** (nobody's resources are gated by it; the command view draws it striped).
- **Presence** = the strength of its outposts on that planet plus its groups stationed there (Patrol or Defend), each by
  hull, guns and crew (the brief's `BestDps` and armour; a space ship's weapons), counted at the controller's think.
- Start holdings come from the power files (`"holds": ["Rime"]`), each with the outposts to place (the world-site and
  base generator work, as today's faction bases).
- A planet changes hands when presence does: an enemy wipes out the defence force and the outposts, or sits there long
  enough with more. Each change is a news line and a relation event (section 5.3).
- **Players hold planets too** (later, with Karl): a player faction's outposts and ships count as its presence, so a
  player can take a planet's resources by force, which is the "combat ship, then take on a faction" step of SURVIVAL.md.
