# Faction controllers and admirals: the two top tiers of the bot brain (design)

Design only (2026-11-17). No code changed, nothing built, nothing run. Written for builders who each take a whole phase
(section 10), a WIP commit after each step. Section 11 holds the open questions for the user (recommendation first),
section 12 what must be agreed with Karl. An answer there overrides the text above it.

The user's idea (2026-11-17): the bot brain gets four tiers: **crew** (built), **captains** (built, one per ship),
**admirals** (new) and a **faction controller** per faction (new). Each faction may have different operating parameters,
and factions may be in conflict or allied with each other in many ways that change through play.

Sources (excerpts): bot-brain.md and its notes, `Source/Crew/`, `Game/GameCrew*.cs`, `FactionTable`, SURVIVAL.md,
linked-logistics.md, COMMAND.md, ROLES.md; the archived story engine for ideas only.

Not verified, for phase 0 of the first builder: how `Msg.Factions` lays out the NPC rows today (section 8 adds a 16 x 16
block to it); whether a faction's ship can live in `SpaceShips` while no bubble holds it (as a player's ship on a trip
does); what a generated vehicle costs to spawn on the 2-core box (a convoy of 4 took 7.2 s: section 8 plans around it);
what holds a planetoid's Game open (`OpenPlace`) and whether the host's main Game can see a closed world's faction bodies.

## 0. What is there today, and the gap

- **Crew** (built): records in a struct of arrays, seats and stations, LOD (bodied, seated, asleep), jobs, boarding.
- **Captain** (built, `CrewWorld.Captain`): one per crewed body; hits, hull and losses into morale; Hold, Engage or Flee.
- **Convoys** (`CrewGround.cs`): followers keep their slot (`ConvoyIndex`, `Lane`) in the leader's frame.
- **Kinds** (`Crew/Factions/*.json`, `CrewFactions.cs`): Traders, Mercenaries, Raiders, Militia, Salvagers, Player Crew,
  Test Hostile; stances to other kinds **fixed at load** (`NpcFactionDef.ToFactions`).
- **Standing** (`GameCrewFactions.cs`): each kind with each player faction (`StoryStanding`, 32 x 16) and lone player
  (`PlayerStanding`, 256 x 16), -100 to 100, saved, sent by `Msg.Factions`; deeds move it; below 0 crews fight; a hit
  raises the victim's enemies' view by a fifth.
- **Player factions** (F2, `FactionTable.Stance`): war, neutral, wants alliance, each way.
- **Call for help** (`GameCrewBoard.cs`): once per boarding, the kind's awake crews within 3 km turn on the boarder.

The gap: nobody decides **why** a ship is where it is. Kinds never change how they see each other, nothing holds a
planet, and no ship works with another but a convoy's followers. The two new tiers give ships a reason.

## 1. The four tiers

| Tier | Decides | Scope | Thinks | Orders go to | Reports go to |
| --- | --- | --- | --- | --- | --- |
| **Faction controller** | Strategy and relations: who is a friend, who is an enemy, which planets to hold or take, what to spend, which missions to run | One faction, the whole system | Every 5 s (real time), one controller a tick round robin | Admirals, as **missions** | (the world: news lines, the Faction window) |
| **Admiral** | Operations for one group: route, formation, which contact to fight, who escorts whom, when to retreat, when to ask for more | One group (1 to 12 ships) | 1 s with a player near, 5 s far; abstract groups at the controller's rate | Captains, as **orders** | Its controller, as **reports** |
| **Captain** (built) | Tactics for one body: engage, hold, flee, boarding acts, which stations to man | One crewed body | 1 s bodied, 5 s far (as now) | Crew, as a **station plan** | Its admiral |
| **Crew** (built) | Work at a station: drive, lay a gun, mend, load, walk | One member | Per tick by its driver (as now) | (keys and acts, as a player's) | Its captain (as now: losses, state) |

**A tier only narrows what the tier below may do; it never presses keys.** "Defend Rime" becomes "hold over the north
outpost in a line, fight hostiles inside 4 km"; the captain still picks Engage or Flee, and rule 1 (crew in the seats)
is untouched. A broken controller costs strategy, never a ship flying with an empty helm.

**Orders down** are small structs written into the tier below (no messages, no allocation): a mission into an admiral's
record, an order into a `CrewShip`. **Reports up** are events pushed into a fixed ring per group (64) and per faction (64)
and read at the reader's next think; a full ring drops the oldest and counts the drop (logged once a minute, never silent).

## 2. Factions: kinds and the factions made from them

Today every Militia ship is one Militia. Factions that differ and fall out fit SURVIVAL.md's "NPC factions are kinds" if
a **faction** (here a **power**) is an actor made from a **kind**:

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
| `caution` | 0.5-2 | Strength ratio it wants before attacking (1.4 cautious, 0.8 reckless); the admirals' retreat line |
| `pride` | 0-1 | How much a loss to someone hurts the relation beyond the loss itself; how hard it is to make peace after defeat |
| `values` | weights | What it cares for: `planets`, `cargo`, `ships`, `crew`, `credits`, `reputation`: the controller's mission scoring and the size of relation events (a Traders power values cargo, so a raided convoy hurts more) |
| `needs`, `wants` | resource lists | (the kind's existing keys, now read) What it must have (fuel, metal: shortage drives Convoy and Take missions) and would like (trade, later) |
| `doctrine` | name | Its admirals' doctrine (section 6.4) |
| `budget` | numbers | Start stock, income per held planet, ship costs by class, the most ships and groups it may field |

Same code, different numbers: Raiders (aggression 0.8, greed 0.9, loyalty 0.2) raid and break deals; Traders (values
cargo, caution 1.4) escort convoys and pay for peace; Militia (values planets, loyalty 0.8) defend and answer allies.

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
| **Patrol** | Always, for each held planet, scaled by its value and the threats seen there | Loops over the planet's routes and outposts; fights what it can beat, reports the rest |
| **Defend** | A held planet or outpost reports an attack, or an enemy group is seen heading for it | Goes there, holds station, fights; calls for reinforcement when outmatched |
| **Convoy** | A need is short at one holding and stocked at another (or a trade run, later) | Moves cargo ships from A to B in formation with escorts; the cargo moves the stock |
| **Escort** | A convoy or a high-value ship has a route through danger | Keeps station on the escorted group, engages what comes near it |
| **Raid** | Aggression and greed against a power (or player faction) it is hostile to: its convoys, routes, outposts | Strikes the target's ships or route, takes loot, leaves before the defence comes |
| **Take** | At war, the target planet's presence is under its strength by its caution | Beats the holder's defence force and outposts, then stays to hold it |
| **Retreat** | Losses past the doctrine's line, or a pact signed | Breaks contact and goes home |
| **Call allies** | A war it is losing, or a defend it can't meet | Sends a request to each power it holds an alliance with (section 5.5); they answer with a mission of their own |

Scoring: `target value (by values) x need or threat x (1 - risk by caution)`, weighed by aggression or loyalty, less
the ships it ties up. A running mission stays unless it falls under half the best new one. Candidates come from its
holdings, enemies' known holdings and routes (16 at most) and allies' calls: about a hundred scores a think, no allocation.

### 3.4 Ships within a budget

A power fields ships from its **stock**: each hull class has a cost (data); income comes from held planets each real
minute; losses are not refunded. Caps: at most `ships` (data, default 24) and `groups` (default 6) a power, and the
world's caps in section 8. New ships appear at a **home** (a held planet's outpost or a station named in the power file),
never in sight of a player (300 m from every person and out of their line of sight, or simply on a closed world).

A ground ship is a vehicle kit role and tier in its kind's style, kept as a **model key** (`VehicleModel.Key`: the same
key makes the same vehicle, so a record needs only its key); a family's marks give a power a recognisable line. Space
ships come from a list of blueprints per role in the power file (Karl's; no space hull generator yet: section 12).

## 4. Territory: who holds a planet

SURVIVAL.md (decided): factions hold resources by holding the planets they are found on. This design proposes how a
power holds one (to agree with Karl, section 12):

- A planet's **holder** is the power with the most **presence** there, if that is at least 60% of all presence there;
  otherwise it is **contested** (nobody's resources are gated by it; the command view draws it striped).
- **Presence** = the strength of its outposts on that planet plus its groups stationed there (Patrol or Defend), each by
  hull, guns and crew (the brief's `BestDps` and armour; a space ship's weapons), counted at the controller's think.
- Start holdings and their outposts come from the power files (`"holds": ["Rime"]`), placed by the existing site tools.
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

Today's player standing is merged, not replaced: a player faction is one more party in a power's row, with the same
bands, events and decay.

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
| Hit a body or crew member (as now) | -5 body, -3 crew | pride |
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
| Keep a pact a real hour | +1 | - |

**Third parties ("word gets around", an idea from the archived story).** An event also moves C's view of the doer if C
**knows** of it (a crew of C's saw it, C holds the planet, or C is the victim's ally): C friendly to the victim takes
`loyalty x amount`, C hostile to it `+amount / 5` (today's `Deed` rule). Raid Traders often and Raiders warm to you.
Knowing is checked once, when the event is posted.

### 5.4 Decay

Every controller think, each relation moves towards its rest value by `forgiveness x 1 point a real minute` (a
grudge of -60 at forgiveness 0.2 takes about four hours of play to fade to its rest). War never decays past Hostile on
its own: leaving a war needs a ceasefire (5.5), so wars don't end by waiting alone. The clock is real (simulated) time,
not the space clock: at 100K a week of game time passes in seconds, and a grudge should not (question 3).

### 5.5 Pacts: ceasefires, alliances, breaking them

A pact table (at most 32 pacts a world): kind, the two parties, its start and an end (or none).

| Pact | Forms when | Effect | Breaks when |
| --- | --- | --- | --- |
| **Ceasefire** | At war, and the loser's losses this war pass its pride line, or both have been at war a long time with nothing taken | Crews don't fight each other for 30 real minutes whatever the band; relation set to -40 | Either attacks the other (a broken pact) or it expires |
| **Non-aggression** | Neutral or better, a common enemy, or a Traders power paying | Crews don't start fights; wary of each other | An attack |
| **Alliance** | Friendly both ways, a common enemy at war with either, and both controllers' loyalty high enough | Fight each other's enemies; answer Call allies; share sightings | An attack; a refused Call allies (relation -10, and an alliance fails after two refusals); the relation falling under Neutral; greed: an ally's weak planet looks better than the alliance (scored, rare) |

A controller proposes a pact at its think when its scoring wants one; the other side's controller answers at its next
think by its own scoring (no talk, no text). With players, pacts are proposed and answered through the Faction window
(phase 5; diplomacy is open in SURVIVAL.md, section 12).

## 6. Admirals and groups

### 6.1 A group

A **group** is 1 to 12 ships of one power (or allies on one Call allies mission) under one admiral, for one mission: a
convoy, a patrol, a strike group, a planet defence force. A fixed pool of `MaxGroups` (64) records: power, mission,
admiral (a crew member and its flagship), members, formation, order, home, strength, losses; **real** while any member
is a body, else **abstract** (section 8). Groups **form** when the controller gives a mission (free ships at a home, or
new ones within budget; the best is flagship) and **dissolve** when it ends (ships go home), when losses pass the
doctrine's break line (survivors Retreat alone), or when the admiral is lost and the doctrine says break (6.5).

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

Tactics, small and bounded: **focus fire** (best threat-to-strength contact), **screen** (escorts between the convoy
and the nearest threat), **retreat** (strength under the enemy's by its caution, or losses past its line), **ask for
reinforcement** (a report; the controller may send another group).

### 6.3 Reports up

Captains push into the group's ring: contact sighted (who, where, strength), ship lost or badly hit, out of
ammunition, boarded (and how it ended), need help. The admiral sums them for the controller: strength, losses, progress,
sightings (into the power's shared sightings, which allies read too).

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

Nothing in the crew system changes but one job name; the existing LOD gives the admiral a body when a player is near.

### 6.6 Phase 2's convoys become groups

`VehicleSpawner.RequestConvoy` stays. A crewed convoy set down becomes a group: the leader (or its command-role vehicle,
ROLES.md's "masts and radio") is the flagship, each member's `ConvoyIndex` and `Lane` its `Form` slot, its out-and-back
drive a `Patrol` with no controller. `CrewGround.cs`'s followers are the `Form` order's ground driver, kept whole. Call
for help becomes a report: the admiral answers first (its escorts), then the controller (another group), then allies.

## 7. What players see and do

- **Faction window (F2).** "How the world's factions see you" gains each power's band in words, its pacts with you,
  the power x power grid (words, hover for the number), planets held, and **news** (the last 32: "Slopolis has fallen
  to the Red Hand"). Phase 5 adds pact proposals for a faction leader.
- **Command view.** A **FACTIONS** layer beside LOGISTICS: planets ringed in their holder's colour (striped when
  contested); groups **you know of** (seen by you, your faction or allies in the last few minutes; the rest is fog) with
  a mission guess (PATROL, CONVOY, STRIKE) and a heading; one heading for your factories, routes or ships red and flagged
  (SURVIVAL.md: threats to a supply line are shown).
- **News lines.** Wars, pacts, planets changing hands, your convoy attacked: a HUD notice and a radio line (one
  templated ASCII line per event kind; no story writing).
- **How players move relations.** By deeds (section 5.3): fighting, capturing, raiding, helping, escorting, releasing
  prisoners; later by contracts (escort, deliver, raid for pay: credits) and pacts through the window.
- **Players as admirals (phase 6).** A player's hired ships are a group with the player as admiral: command view orders
  (Form on me, Escort, Engage, Hold, Retreat) go through the same admiral code. SURVIVAL.md's "fleet room" idea.

## 8. Scale: few minds, many crew, far-away groups as one

- **Counts and budget.** 16 controllers (one a tick in turn, each every 5 s), 64 admirals (two a tick round robin);
  thousands of crew under their own LOD, unchanged. Controllers plus admirals at most 0.3 ms a tick on the 2-core box,
  nothing allocated in a steady tick (fixed arrays throughout), measured by a new `--faction-check` (a dev tool, not a
  self-test section).
- **Abstract groups.** A group with no body near a player is one record (position, speed, strength, cargo) moved by
  the space clock (between planets as one nav trip of its slowest ship; on a closed world along its route); its ships
  are records (model key, hull share, crew alive), as the link ledger keeps a closed world. **Abstract fights** are
  settled at the controllers' rate by strength ratio, a bounded share lost per think (minutes, never one roll), as news.
- **Materialising.** Within 5 km of a player, a group's ships are made as crewed bodies out of sight, a few a second
  (generation is slow: a power's model keys are made once and cached as blueprints); hull share becomes damage spread
  over parts, crew alive the roster. Nobody within 7 km for a minute: they **collapse** to a record. At most
  `MaxFactionBodies` (40) at once; groups beyond that stay abstract and keep their distance.
- **Closed worlds.** The layer runs while the host does (Karl's rule holds), as records in the host's main Game; a
  closed world is never opened for a faction (the link ledger's answer 5).
- **Network.** Server only. Clients get power names, colours, the grid (256 bytes on change) and pacts and holders in
  `Msg.Factions` (protocol bump); known groups per client (at most 32, 1 Hz while the view is open, filtered by what
  their faction knows) and news lines, new messages at the end of `Msg`. Missions, scores, budgets and unseen groups
  are never sent.
- **Saves.** A new `Powers` section (a tag beside `CrewTag`, versioned, skipped with a log line if unreadable):
  relations, pacts, holdings, stocks, missions, groups and abstract ships, news; ids as names or keys. No new save file
  (nothing for `SavePaths`); old saves start every power from its file.

## 9. Reuse, and what is not touched

**Reused:** crews, captains, drivers and LOD as they are; kinds and their files; the NPC rows and `Msg.Factions`; `Deed`
and call for help (grown); `CrewGround`'s followers; the vehicle kit's roles, model keys and convoys; `GameNav` orders and
trips; the link ledger's pattern for closed worlds; the command view's layers.

**Not touched:** the production system (TwitchTheSniper's: a power's stock is abstract), maps and terrain (another
developer's: outposts use the existing site tools), the archived story, player factions' stances to each other, Karl's
flying and gunnery.

## 10. Phases (one builder each, the whole phase; a WIP `[skip ci]` after each step)

Value first: phase 1 alone makes the world's factions fall out and make up through play, with what is already built.

| Phase | What | Done when |
| --- | --- | --- |
| **1. Living relations** | Powers (`Crew/Powers`, a kind per power when the folder is missing), the power x power grid, bands with hysteresis, `Crew/Relations.json` events, third parties who know, decay, pacts (ceasefire, non-aggression, alliance) between powers by simple rules, crews fighting by the grid in place of `ToFactions`; news list; Faction window grid and news; `Powers` save section; `Msg.Factions` grid (protocol bump); `--faction-check` (a scripted run of events, printing the grid) | Two Militia powers and a Raider power: raids sour relations, a common enemy makes an alliance, a broken pact spreads; allied crews fight together in `--crew-check`; the grid survives a save and load; a joined client sees it |
| **2. Groups and admirals** | Group records, the admiral (a crew record with job `Admiral` on a flagship), orders into `CrewShip` (Form, GoTo, Engage, Escort, Hold, Retreat), reports and rings, doctrine data, succession, convoys from the spawner as groups, call for help through the admiral | A spawned convoy with escorts holds formation, focuses fire, retreats on losses; killing the admiral's flagship makes a captain step up (Militia) or the group scatter (Raiders) |
| **3. The controller in open worlds** | Parameters, assets, stock and income, mission scoring, groups formed for missions, ships made from model keys at homes within budget and caps; Patrol, Defend, Escort, Convoy, Raid, Retreat, Call allies; allies answering | Near a player, a Traders power runs an escorted convoy between two of its places and a Raider power raids it; the Militia defends when called; spending stays in budget |
| **4. Territory and the far layer** (after Karl agrees section 4) | Presence and holders, start holdings and outposts, Take, abstract groups and abstract fights, materialising and collapsing, caps; the command view's FACTIONS layer with fog and threats flagged | A planet changes hands while no player is there and the news says so; a group materialises out of sight as a player comes near and collapses when they leave; 16 powers, 64 groups under 0.3 ms |
| **5. Players in the politics** | Pacts with player factions through the Faction window (leader only); contracts for credits (escort, deliver, raid); allied NPC crews helping players | A player faction signs a ceasefire with a power at war with it, then breaks it, and its allies hear |
| **6. Player admirals** | A player's own crews as a group; group orders in the command view | A player orders three hired ships to escort their mining ship and they hold formation and fight |

## 11. Open questions for the user (recommendation first)

1. **Factions made from kinds.** Recommend: powers (`Crew/Powers`) made from kinds, so two Militias can fight; at most
   16 powers (the table's NPC rows); with no Powers folder, one power per kind. Or: a kind is a faction, one each (simpler,
   but every Militia shares one grudge).
2. **The admiral.** Recommend: an abstract mind tied to a real crew record on a flagship (killable, a boarding prize,
   succession by doctrine). Or: wholly abstract (no target for the player).
3. **The faction clock.** Recommend: controllers, decay and pacts on real (simulated) time; abstract groups move on the
   space clock. Or: everything on the space clock (at 100K a war could start and end while a ship coasts).
4. **Fights nobody sees.** Recommend: abstract groups fight by strength, bounded, reported as news, never on a world
   that isn't open. Or: nothing moves or fights where no player is (stricter, but the map never changes by itself).
5. **Can a power die?** Recommend: no: a power with no planets and no ships rebuilds slowly at a home in data, so the
   system never empties; a power file may say `"canFall": true`. Or: powers can be wiped out, and later an independent
   ship "making a name" (SURVIVAL.md) could found one in a free row.
6. **What pays for a power's ships.** Recommend: an abstract stock with income per held planet (data), not the production
   system. Or: powers run real factories (leans on TwitchTheSniper's work, far costlier).
7. **What players know.** Recommend: fog: only groups seen by you, your faction or your allies in the last few minutes;
   relations and holdings are known to all. Or: everything shown (easier to read, no scouting).
8. **Faction bodies at once.** Recommend: 40 a world, groups beyond that kept abstract and at a distance. Or: more, at
   the bodies' cost.
9. **Allied NPCs and players.** Recommend: with an alliance pact, a power's crews fight a player's enemies near them and
   answer that player faction's Call allies (a button in the Faction window, once every few minutes). Or: NPC allies only
   leave you alone.

## 12. To agree with Karl

- **Planet holding (section 4):** presence (outposts and stationed groups), the 60% line, contested planets gate nothing,
  how a planet is taken, and whether players hold planets too. SURVIVAL.md lists "which resource each planet holds" and
  "how a faction defends its planet" as open: this proposes patrols, outposts and defence forces.
- **Diplomacy:** open in SURVIVAL.md. This proposes numbers with bands, events, decay and three pacts, between powers
  and later with player factions. Is that the shape he wants, or should it wait?
- **The command view:** a FACTIONS layer (his view) with fog and threat flags, and later group orders for a player's own
  fleet (his "fleet room" idea).
- **Ships for space groups:** which of his blueprints a power fields per role (frigate, miner, escort), since the vehicle
  kit makes ground vehicles only; a command fit for a flagship.
- **SURVIVAL.md:** if he agrees, "faction politics" moves from ideas to decided, with a line pointing here, and "NPC
  factions are kinds" gains "powers are made from kinds".
