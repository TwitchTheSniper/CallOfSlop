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

## 5. Relations that change through play

### 5.1 One table for everyone

Relations are numbers from -100 to 100, kept by the host in the factions table beside what is there now:

| Pair | Where | New? |
| --- | --- | --- |
| Power x power | `NpcRelation[16 * 16]` (sbyte), not symmetric: how A sees B | New (256 bytes) |
| Power x player faction | `StoryStanding[32 * 16]` (to be renamed `NpcStanding`) | There |
| Power x lone player | `PlayerStanding[256 * 16]` | There |
| Player faction x player faction | `FactionTable.Stance` (war, neutral, wants alliance): the players' own choice, untouched | There |

So today's player standing is merged rather than replaced: a player faction is one more party in a power's row, with
the same thresholds, events and decay. Nothing about player factions' own stances to each other changes.

### 5.2 Thresholds (with 5 points of hysteresis each way)

| Band | Value | What crews and controllers do |
| --- | --- | --- |
| **War** | -100 to -50 | Fight on sight; the controller may Raid and Take against them; no pacts but a ceasefire |
| **Hostile** | -49 to -1 | Fight on sight (today's "below 0"), no raids on their planets |
| **Wary** | 0 to 19 | Answer fire only (today's wary); no trade |
| **Neutral** | 20 to 59 | Leave alone; trade (later); may answer a call for help against a common enemy |
| **Friendly** | 60 to 100 | Help in a fight they see; share sightings; a pact may be offered |

**Allied** is a pact, not a band: two powers (or a power and a player faction) are allied while an alliance pact holds,
whatever the numbers, and an ally's crews treat each other's enemies as their own in a fight. The bands keep the present
rule that a crew fights below 0, so phase 1 changes no existing behaviour for a player.

Starting values come from data: the kind's `stance.factions` words keep working (hostile -50, wary 0, neutral 20, as
`StartStanding`), and a power may give numbers (`"relations": {"Rime Home Guard": -70, "players": 0}`). Each value also
has a **rest** value it drifts back to (the starting one, unless the power file says otherwise).

### 5.3 Events that move them

One table in data, `Crew/Relations.json`: each event, its base amount, and which of the power's parameters scales it.
Proposed starting numbers (to tune):

| Event | To the victim's view of the doer | Scaled by |
| --- | --- | --- |
| Hit a body or crew member (once a minute per pair, as now) | -3 to -5 | pride |
| Kill a crew member | -4 | values.crew |
| Destroy a ship | -15 | values.ships, pride |
| Capture a ship (boarded, taken) | -20 (as now) | values.ships, pride |
| Raid a convoy (cargo lost) | -10 per share of cargo | values.cargo, greed |
| Take a planet | -40 | values.planets, pride |
| Release or drop prisoners | +5, +3 (as now) | forgiveness |
| Help in a fight (damage dealt to the victim's attacker while near it) | +2 a minute, at most +10 a fight | loyalty |
| Escort its convoy to the end | +5 | values.cargo |
| Trade (later: a sale or a delivered contract) | +1 to +5 | greed |
| Break a pact | -40, and -15 to every power that knows | loyalty of the hearer |
| Keep a pact a whole game day | +1 | - |

**Third parties ("word gets around", from the archived story's patterns, as an idea only).** An event between A and B
also moves C's view of the doer if C **knows** of it: C has a crew within sight of it, holds the planet it happened on, or
is A's ally (allies tell each other). C's share: its own relation to the victim, as a sign: C friendly to the victim
takes `loyalty x amount`; C hostile to the victim takes `+amount / 5` (the present `Deed` rule, kept). This is how a
player who keeps raiding Traders becomes friends with Raiders without asking, and how killing a Militia escort sours the
Militia's allies. Knowing is checked once, when the event is posted to the faction ring, not every tick.

### 5.4 Decay

Every controller think, each relation moves towards its rest value by `forgiveness x 1 point a real minute` (a
grudge of -60 at forgiveness 0.2 takes about four hours of play to fade to its rest). War never decays past Hostile on
its own: leaving a war needs a ceasefire (5.5), so wars don't end by waiting alone. The clock is real (simulated) time,
not the space clock: at 100K a week of game time passes in seconds, and a grudge should not (question 3).

### 5.5 Pacts: ceasefires, alliances, breaking them

A pact table (at most 32 pacts a world): kind, the two parties, its start and an end (or none).

| Pact | Forms when | Effect | Breaks when |
| --- | --- | --- | --- |
| **Ceasefire** | At war, and the loser's losses this war pass its pride line, or both have been at war a long time with nothing taken | No fighting for 30 real minutes; relation set to -40 (Hostile, no raids) | Either attacks the other (a broken pact) or it expires |
| **Non-aggression** | Neutral or better, a common enemy, or a Traders power paying | Crews don't start fights; wary of each other | An attack |
| **Alliance** | Friendly both ways, a common enemy at war with either, and both controllers' loyalty high enough | Fight each other's enemies; answer Call allies; share sightings | An attack; a refused Call allies (relation -10, and an alliance fails after two refusals); the relation falling under Neutral; greed: an ally's weak planet looks better than the alliance (scored, rare) |

A controller proposes a pact at its think when its scoring wants one; the other side's controller answers at its next
think by its own scoring (no talk, no text). With players, pacts are proposed and answered through the Faction window
(phase 5; diplomacy is open in SURVIVAL.md, section 13).

## 6. Admirals and groups

### 6.1 A group

A **group** is 1 to 12 ships of one power (or of allies on one Call allies mission) under one admiral, made for one
mission: a convoy, a patrol, a strike group, a planet defence force. A fixed pool of `MaxGroups` (64) records:

- its power, mission, admiral (a crew member id and its flagship), members (ship records or bodies, at most 12),
  formation, current order, home, its strength (summed at each think) and its losses this mission;
- **real or abstract** (section 8): real while any member is a body in an open bubble, abstract otherwise.

Groups **form** when the controller gives a mission: it takes free ships at a home (or orders new ones within budget)
and names the best of them flagship. They **dissolve** when the mission ends (ships go home and become free), when
losses pass the doctrine's break line (survivors Retreat as singles), or when the admiral is lost and the doctrine says
the group breaks (6.5).

### 6.2 What an admiral decides

Each think the admiral reads its mission, its members' reports and its contacts (the union of its captains' contact lists
and its power's shared sightings), and writes one **order** into each captain's `CrewShip`:

| Order | The captain does |
| --- | --- |
| `Form(slot)` | Keeps a slot in the flagship's frame (the convoy's `ConvoyIndex` and `Lane`, generalised to an offset); its own goal stays Hold |
| `GoTo(place)` / `Route(route)` | Flies or drives there (a `GameNav` order for a ship in a star system, the ground helm's path for a vehicle) |
| `Engage(target)` | Fights that contact first (its own pick only if it can't reach it) |
| `Escort(body)` | Keeps station on that body and engages what comes within its band |
| `Hold` | Holds where it is, fights what comes |
| `Retreat(place)` | Flees to that place, whatever its own morale (the captain's Flee, given a heading) |

The captain keeps its own judgement inside the order: it still Flees on its own hull and morale lines, unless the
doctrine's `discipline` holds it (a disciplined Militia line holds until the admiral says go; Raiders run when they like).

Tactics, all small and bounded: **focus fire** (the admiral picks the contact with the best threat-to-strength ratio for
most of its ships, leaves escorts on their charge), **screen** (escorts between the convoy and the nearest threat),
**retreat** (when the group's strength falls under the enemy's by its caution, or losses pass its line), **ask for
reinforcement** (a report to the controller, which may give a Defend or Escort mission to another group).

### 6.3 Reports up

Per group, pushed into its ring by captains and the admiral: contact sighted (who, where, strength guess), ship lost,
ship badly hit, out of ammunition, boarded (with the boarding acts' outcome), mission done or failed, need help. The
admiral sums them into its own report to the controller at each think: strength left, losses, sightings (fed into the
power's shared sightings, which allies also read), progress.

### 6.4 Doctrine (data, per power: `Crew/Doctrines/*.json` or inline)

| Key | What |
| --- | --- |
| `caution` | Strength ratio before engaging (from the power) and the retreat line |
| `discipline` | 0-1: how far captains' own Flee is held off while the admiral holds |
| `skill` | 0-1: how well it picks focus targets and formations (a low skill picks the nearest, not the best) |
| `formation` | Its preferred shapes per mission: column, wedge, line, screen; spacing |
| `breakLine` | Losses (share of the group) at which the group retreats or breaks |
| `succession` | `captain` (the next senior captain steps up after a delay), `break` (the group scatters), `flagship` (only a named flagship class can carry an admiral; the rest return home) |
| `delay` | Seconds of disarray while command passes: no new orders, each captain on its own |

### 6.5 Is the admiral a person?

Recommended: **an abstract mind carried by a real crew record.** The admiral's thinking is a group record (cheap, no
body needed), but it is tied to one crew member aboard the flagship (a record in `CrewWorld` like any other, job
`Admiral`, at the bridge seat or a Gunnery Console used as a command station, else in a couch). So:

- killing that member or destroying the flagship ends that admiral: **succession** by doctrine (a captain steps up after
  `delay`, or the group breaks);
- an admiral who surrenders in a boarding is a **prize**: a prisoner worth a big ransom in credits (later, with trade),
  and a relation event (-25) for the power; releasing one is +15;
- the look line names them ("Admiral Vance, Rime Home Guard"), so a player can pick the flagship out.

Nothing in the crew system changes for this but one job name. The alternative, a fully abstract admiral, is cheaper by
nothing measurable and gives the player no target; a fully bodied admiral walking about is the same as the recommended
one once bodied by the existing LOD.

### 6.6 Phase 2's convoys become groups

`VehicleSpawner.RequestConvoy` stays as it is. When a crewed convoy is set down, it becomes a group: its leader is the
flagship, its leader's commander the admiral (the command role's vehicle if the convoy has one: ROLES.md's command role
is "masts and radio, armour", made for this), each member's `ConvoyIndex` and `Lane` its `Form` slot, its out-and-back
drive a `Patrol` mission with no controller (a convoy from the Spawn tab has a power but needs no mission). The follower
code in `CrewGround.cs` is the `Form` order's ground driver; nothing there is thrown away. Today's Call for help becomes a
report: the group's admiral answers first (its own escorts), then the power's controller (another group), then allies.

## 7. What players see and do

- **Faction window (F2).** "How the world's factions see you" stays and gains: each power's band in words and colour,
  its pacts with you, its relation to the other powers (a 16 x 16 grid in words, hover for the number), what planets it
  holds, and a short **news** list (the last 32 events: "Rime Home Guard and the Red Hand are at war", "Slopolis has
  fallen to the Red Hand", "The Free Traders' convoy reached Shard"). Phase 5 adds proposals (ceasefire, non-aggression,
  alliance) for a faction leader.
- **Command view.** A **FACTIONS** layer beside LOGISTICS: each planet ringed in its holder's colour (striped when
  contested); groups **you know of** as markers in their power's colour with a guess at their mission (PATROL, CONVOY,
  STRIKE) and a line to where they go; a group heading for one of your linked factories, routes or ships drawn red and
  flagged (SURVIVAL.md, decided: threats to a supply line are shown). You know of a group if a ship of yours (radar,
  sight), an allied power or an allied player has seen it in the last few minutes; the rest is fog.
- **News lines.** Big events (war declared, a pact made or broken, a planet changing hands, a convoy of yours attacked)
  come as a HUD notice and a radio line (no story writing: one templated line per event kind, ASCII).
- **How players move relations.** By deeds (section 5.3): fighting, capturing, raiding, helping, escorting, releasing
  prisoners; later by contracts (escort, deliver, raid for pay: credits) and pacts through the window.
- **Players as admirals (later, phase 6).** A player's own hired fleet (bot-brain.md's own crews) is a group with the
  player as its admiral: the command view's orders gain Form on me, Escort, Engage, Hold, Retreat for a group of picked
  ships, and the same admiral code turns them into captain orders. This is the "fleet room" idea of SURVIVAL.md.

## 8. Scale: few minds, many crew, far-away groups as one

- **Counts.** At most 16 controllers (one tick each in turn: each thinks every 16 ticks at most, every 5 s by rule),
  `MaxGroups` 64 admirals (two a tick round robin, so every one thinks within half a second when due), thousands of crew
  unchanged (the crew system's own LOD and budget stand).
- **Budget.** Controllers plus admirals at most 0.3 ms a tick on the 2-core box, nothing allocated in a steady tick (fixed
  arrays: groups, missions, pacts, rings, relation table). Measured with 16 powers, 64 groups and the crew budget's
  thousands, by a new `--faction-check` (a dev tool, not a self-test section).
- **Abstract groups.** A group with no member in an open bubble near a player is **abstract**: it is one record with a
  position, a speed, a strength and a cargo, moved along its route by the space clock (between planets as a single nav
  trip of its slowest ship; on a closed world along a route at its cruise speed). Its ships are records (model key,
  hull share, crew alive count), as the link ledger keeps a closed world as a report. **Abstract fights** between two
  abstract groups that meet are settled at the controllers' rate by strength (each side loses a share of ships by the
  other's strength over its own, bounded, so a battle takes minutes, never one roll), and logged as news.
- **Materialising.** When an abstract group comes within 5 km of a player (or a bubble a player is in opens where it
  is), its ships are made as bodies, crewed, out of sight where it can be (behind the horizon, in space at range), a few
  a second (a generated vehicle is slow to make: `VehicleModel` keys of a power's fleet are made once and cached as
  blueprints). Their hull share becomes damage spread over parts; the crew alive count becomes the roster. When nobody
  is within 7 km for a minute, they **collapse** back into a record. At most `MaxFactionBodies` (40) faction bodies a
  world as bodies at once; groups beyond that stay abstract and keep their distance.
- **Closed worlds.** Karl's rule (nothing happens while the game isn't running) holds; the faction layer runs while the
  host does, on closed worlds too, but only as records in the host's main Game: a closed world is never opened for a
  faction's sake (the link ledger's answer 5, kept).
- **Network.** All of it is server only. Clients get: the power names, colours and the relation grid and pacts in
  `Msg.Factions` (the grid is 256 bytes, sent on change; protocol bump), planet holders (a byte per body in the space
  message or `Msg.Factions`), known groups for the command view per client (at most 32 entries, 1 Hz while the view is
  open, filtered by what that player's faction knows: a new message at the end of `Msg`), and news lines (a new message).
  Missions, scores, budgets and unseen groups are never sent.
- **Saves.** A new `Powers` section in the world save (a tag beside `CrewTag`, versioned, skipped with a log line if it
  doesn't read): relations, rest values, pacts, holdings, stocks, missions, groups with their abstract ships and
  positions, rings' unread events, news. Power, mission and group ids saved as names or keys, as crews' factions are.
  No new save file: nothing for `SavePaths`, the package list or MIGRATING.md. Old saves start every power from its file.
