# The bot brain: one AI for crews, pilots, NPCs and your own bots (design)

Design only (2026-11-16). No code changed, nothing built, nothing run. Written for builders who each take a whole phase
(section 15), a WIP commit after each step. Section 16 holds the open questions for the user (recommendation first), section
17 what must be agreed with Karl before phase 1 touches his ship crew. An answer there overrides the text above it.

The user's ask (2026-11-16): one design covering all AI, bots and crew in Sandbox, so the separate systems become one.

Sources: the files and docs named in section 1, read in excerpts.

Not verified, for phase 0 of the first builder: what one bodied bot `Player` costs a tick on its own (movement, sight, aim) with
no arena brain; whether a bot `Player` can sit through `ConstructEdits.Use` and be carried by `SandboxHands` like a person
(nothing does it today: no bot sits anywhere); whether `CPart.Keys` reaches clients (if it does, an occupied seat already shows);
whether `Msg.Factions` still carries `StoryNames`/`StoryStanding` with the story off (section 7 reuses them).

## 0. The user's binding rules

1. **Driving rule (2026-11-07).** A faction (AI) vehicle moves only while a bot sits in its driver's seat. The AI may drive
   it directly, but the seated bot is required: kill or pull the driver and it stops (throttle 0, brakes on). Same for
   gunners: a gun fires only with a bot in the seat or station that works it. Players' own remote, logic and autopilot
   driving is unaffected.
2. **Boarding (2026-11-16).** NPCs aboard a ship being boarded have a set of actions, from fighting to scuttling the ship
   if desperate, chosen by their faction's rules. The player chooses what to target and how much to blow up, and may kill
   crew from outside first so they can't scuttle or fight back.
3. **Faction rules are data** (the story engine's factions are archived).

## 1. What exists today

| Thing | What it does today | Body? |
| --- | --- | --- |
| Ship AI crew (Karl, `ConstructShipAi.cs`, `GameSandboxShipAi.cs`) | A Flight Stabilizer setting (Off, Fights strangers, Fights everyone) that writes the ship's seats' keys and aim (`ShipAi.Seat`): flies, lays and fires turrets, launches racks, fires flares at the nearest player in 2.5 km; a player at the helm stands it down; not saved | None: seats get `CPart.Occupied` with no sitter |
| Aircraft pilots (`GameSandboxAircraft.cs`, `AircraftPilot.cs`) | Hold, Patrol, Goto, Return, Land through the stabilizer's autopilot; 32 at most | None |
| Spawned bots (`GameSandboxSpawns.cs`) | The arena `BotBrain` on foot; each a `Player` with an id above the people's (`SpawnBotCapMax = 224 - MaxPeople`) | A full `Player` |
| Faction vehicles, convoys (`VehicleSpawn`, `VehicleBrief*`) | Set down with a **brief** (guns and arcs, seats and what each controls, weak points, keep-away side, steering, entries, loot). Nothing reads it; nothing drives them | None |
| Mass horde, robot shells, sentries | Zombies in three tiers; a player wearing a shell; a gun with a proximity sensor firing by itself | - |
| Breakdowns (`GameSandboxBreakdowns.cs`) | Crewed player ships' drives warn then fail; mended at the part or the Engineer's Panel; ships with an AI crew skipped | - |
| Story people, HTN (archived) | Utility-scored goals planned by a bounded, allocation-free HTN; a few think per tick | Had bodies |
| Factions (`Factions.cs`) | 32 player factions by owner byte; the archived story's `StoryNames`, `StoryStanding` (16 x 32) still in the table | - |
| Crew fittings, construct frame, linked logistics | Couches, Gunnery Console, Charging Cradle; one frame per moving construct; route orders and the Dispatcher (design) | - |

The finding: four brains (ship crew, aircraft pilot, arena bot, horde) that share no perception, no targets and no idea of a
faction; none puts a body in a seat; ground faction vehicles have no driver at all. Karl's crew is the best of them and
the right driving code to keep: what changes is who is allowed to press the keys.

## 2. What a player sees

- **An enemy ship has people in it.** A pilot sits at the helm (visible through a canopy or open bridge), gunners sit at
  their turrets and turn with them. Shoot the pilot through the glass and the ship stops flying: throttle 0, it holds
  where its stabilizer can hold it (section 16, question 2); a few seconds later another crew member climbs into the
  helm, unless you killed them too or the helm is wrecked. Shoot a gunner and that turret falls silent until someone
  takes the seat.
- **Convoys drive.** Trucks follow their leader in column or wedge, gunners on the beds track you. Kill a driver and
  that truck rolls to a stop, brakes on; a passenger may get out and take the wheel, which is the moment to shoot them.
- **Boarding is a fight with choices** (section 8): crew close doors and hold rooms, a desperate crew runs for the
  magazine to scuttle the ship, and a look line counts it down; kill the engineer on the way or disarm the charge.
  A beaten crew surrenders, hands up; take the helm and the ship is yours.
- **Your own crew** (section 9): Robot Shells you built stand in their cradles; assign one to a gunnery console or the helm
  in the Crew window and it works that station for you, by the same brain. Your crew never stops you driving by hand.

## 3. The brain's shape

One server-only system, `Source/Crew/` (a new folder; `Game` side in `Game/GameCrew*.cs` partials). Five pieces:

1. **The roster** (per body). Each crewed construct has a `CrewRoster`: fixed arrays, at most `MaxCrewPerBody` (32)
   members. A member: a stable id, faction, kind (human, robot), skill (0-1), health, carried gun and ammo, job, station
   (a part tag, as the brief's `BriefRef`), state (at station, walking, fighting, surrendered, dead), morale, and a body
   link (the bot `Player` id when bodied, else none). A member belongs to one body at a time; boarding parties and transfers
   move them between rosters. Crew on foot with no body (outpost guards) belong to a ground roster for their site.
2. **Stations** (per body, derived, cached by `RebuildStamp`). What can be worked and from where: the helm (the drive seat,
   or the seat its Flight Stabilizer reads: `ConstructFlight.SeatFor`), each gunner's seat (`CGun.Seat`), launch buttons
   (Buttons wired to Ordnance Racks or flare launchers, as `ShipAi.Scan` finds them; worked from the nearest seat that
   reaches them, else standing at the button), the Engineer's Panel and each works piece (engineer), magazines and autoloader
   feeds (loader), Loader Arm and Bridge Crane seats (crane operator), Cargo Docks (hauler), Crash Couches and Charging
   Cradles (rest). A generated vehicle's brief already lists `Seats[]` with what each controls: use it; anything else gets
   the same scan Karl's `ShipAi.Scan` does today, generalised.
3. **Perception** (per body, not per member). One contact list per crewed body, refreshed every 0.5 s bodied, 2 s far:
   players, crewed bodies and on-foot crew within its sight range (2.5 km as now; radar and lock-on widen it when those
   parts exist), each with position, velocity, faction and threat (the brief's `Engage` and `BestDps` for a generated
   vehicle). Line of fire stays per gun and per shot (Karl's own-hull check). Members don't each raycast for targets: a
   bodied member on foot uses the arena bot's sight (`BotCombat`) only in a fight on foot.
4. **Faction rules** (data, section 7): who is an enemy, when to fight, flee, surrender or scuttle, job priorities, crew
   sizes, skill, what capture yields.
5. **Deciding**, at two levels:
   - **The captain** (one per crewed body, no body of its own: it is the roster's mind). Utility scoring, at 1 Hz bodied
     and 0.2 Hz far, over body goals: Engage, Hold, Patrol, Follow (a convoy leader, an escort), Route (orders from
     `GameNav`, a route, the Dispatcher), Land, Flee, Repel boarders, Abandon, Surrender, Scuttle. Weights and thresholds
     come from the faction file; inputs are the contact list, hull and works health, crew strength, ammunition, morale.
     The winning goal sets a **stance** and a **station plan**: which stations must be manned, in priority order.
   - **The members.** Each takes the highest unmet station in the plan it can reach soonest (a small greedy assignment,
     re-run when the plan or the roster changes, never per tick), then runs that job's **method**: a short fixed list of
     steps from a table (`Crew/Jobs.json`): walk to the station, sit or stand at it, work it; on a fault (blocked,
     seat taken, station broken) the next method. This is the story HTN's shape cut down: no world-state trial, no
     backtracking over relations, bounded steps, nothing allocated. The story's planner code stays archived and untouched.
   - **Station drivers** do the work, one per station kind, and are where today's code goes: `HelmDriver` for ships
     (Karl's flying from `ShipAi.Step`, unchanged in what it presses), for aircraft (`AircraftPilot`'s orders, unchanged),
     for ground vehicles and walkers (new: section 6); `GunDriver` (Karl's laying, leading and bearing rules, racks and
     flares); `EngineerDriver`, `LoaderDriver`, `CraneDriver`, `HaulerDriver`. A driver writes only what a player in that
     seat could: the seat's keys and aim, and the same server acts a player's tool sends (`MendPay`, a door's use).

**The rule-1 gate is one line in one place.** A driver may write a station's keys only while a living member of the roster
is at that station (bodied: the bot `Player` is its sitter; abstract: the roster says so). Every tick the crew system
clears the keys of a faction body's helm and gunner seats that have no member at them, so a faction vehicle with an empty
helm gets throttle 0 and brakes on, and its guns are not fired. Players' own bodies never pass through this gate: a
player's logic, remote, autopilot and sentries run as today.

## 4. Crew bodies, and thinking less far away (LOD)

Crew come in three tiers, like the horde, so 200 crew cost what about 60 do:

| Tier | What it is | When | Costs |
| --- | --- | --- | --- |
| **Bodied** | A bot `Player` (an id from the spawned bots' range) with a crew mind instead of the arena `BotBrain`: real movement, hit boxes, animation, network, seats, tools | Within 300 m of any living player, or aboard a body a player is aboard or touching, or in a fight with a player's body under 600 m | The engine's own per-bot cost plus its job; capped at `MaxBodiedCrew` (96, shared with spawned bots out of `SpawnBotCapMax`) |
| **Seated** | A roster record at its station, no `Player`. Drivers run, at a coarser rate: helm every 2nd tick, guns every 4th, others 1 Hz | A crewed body further than that, still within 3 km of a player or flying a route | A few microseconds a member |
| **Asleep** | The record only; nothing thinks | The body asleep or past 3 km from every player and not on a route | Nothing |

- **Promotion** makes a bot `Player` sitting in its seat (or standing at its station spot in the body's frame, so it takes
  the body's motion: `ConstructRide.IntoMotion`), with its record's health and gun. **Demotion** removes the `Player`
  and keeps the record; a member walking is put at the end of its walk. Hysteresis: promote at 300 m, demote at 380 m.
- **Out of bodies:** when every bodied slot is taken the nearest members to players get them; the rest stay seated. A
  seated member a player shoots at is promoted first.
- **Hits on seated crew** (a railgun from 2 km, a torpedo): a member dies with the station part it sits at, and a blast
  that reaches a station (the existing blast's parts list) hurts its member by the same amount a player there would take.
- **Network** follows from this: clients see bodied crew as they see any bot today, and seated crew not at all (a figure
  in a cockpit 300 m off is a few pixels: section 16, question 9 asks whether far seats should draw a stand-in).

Budget (to measure in phase 1): 200 crew in a world, 96 bodied. The crew system's whole tick at most 1.5 ms of the
8 ms, with bodied bots' movement counted where `Player`s are already counted. Per tick work is bounded per body
whatever its size (stations scanned once per rebuild, the plan re-run on change only, contact lists on a stagger: one
body's refresh per tick round robin, as `ShipAiFind` does once a second). Fixed arrays per roster, pooled minds; nothing
allocated in a steady tick (the self-tests measure it).

## 5. Moving inside a construct

Crew walk the body's own frame (construct-frame-notes.md: one frame per construct, 2 m round its bounds, lifts and
turntables carry riders). They need a path inside:

- **CrewNav**, one walk graph per body in its rest frame: nodes on standing spots (the deck tops `--ride-check` already
  finds), at stations, doors, hatches, ladders and lift decks; edges where a standing person fits (1.9 m head room,
  0.6 m width; the building check's reach rules, `--check-building`, already answer "can a standing person reach every
  room, door, seat and stair": reuse that walker). Built off the tick in slices, as the brief is, cached by
  `RebuildStamp`; a destroyed part cuts the edges through it (a patch, not a rebuild). Generated interiors
  (`InteriorPlanner`) hand it their rooms and doors as hints, so a Titan's decks need no search.
- **Bodied members** walk it with the arena bots' path follower (`BotFollow`, `BotUnstick` shapes, ported to rest-frame
  points turned to world each tick), open doors by the door's use, ride lifts as players do.
- **Seated members** "walk" as a timer: path length over walking speed, then they are there.
- A member who can't reach a station (cut off by a breach, a locked door) takes the next it can reach. This is also what
  makes boarding tactical (section 8): kill the crew between the engine room and the magazine and nobody can scuttle.

## 6. Jobs

| Job | Station | What the driver does | Phase |
| --- | --- | --- | --- |
| **Pilot** | Helm (drive seat / stabilizer seat) | Ships: Karl's flying. Aircraft: `AircraftPilot`'s orders. Ground: follow a path, a leader, keep the brief's `KeepAway` side off the enemy, hold `Engage` range for its gunners, brake to stop. Walkers and giants: the same through the Driving Module's keys, slower turn caps | 1 (ships, air), 2 (ground) |
| **Gunner** | A gunner's seat or Gunnery Console | Karl's laying and firing; launch buttons; flares. Ground: the brief's `Clear` bins before firing | 1 |
| **Engineer** | Engineer's Panel, works pieces, fires | Sees to breakdown warnings, mends broken works pieces (same act and cost as a player's), puts out fires; in a boarding, the one who scuttles | 4 |
| **Loader** | Magazine, racks, autoloader feed | Carries reloads to a rack or feed by hand when a gunner runs low | 4 |
| **Hauler** | Store, machines, Cargo Dock | Carries crates and pieces inside a body ("players and bots carry things by hand", INTERIORS.md), works a dock's exchange | 7 |
| **Crane operator** | Loader Arm, Bridge Crane seats | Pick and put by the lifters' points | 7 |
| **Guard / marine** | A room or choke point | Holds it with a hand gun; repels boarders | 5 |
| **Spotter** | Beside a capital gun | Shares a target's range and lead with its gunners (tighter bearing tolerance) | 7 |
| **Builder drone** | A building station's bay | Works a station's job beside its arms (later, its own design) | 7 |
| **Passenger / rest** | Couch, cradle, galley | Spare crew wait here and fill a station that empties | 1 |

Spare crew are what make rule 1 fair: a ship with 6 crew and 4 stations survives losing a pilot once; a two-seat buggy
does not.

## 7. Faction rules as data

NPC factions are kinds (SURVIVAL.md: traders, mercenaries, details not fixed). Their home is a new data folder beside
the construct data, loaded at start-up like any other, a bad file refused with a reason:

- `Crew/Factions/<Name>.json` (`"format": "call-of-slop-npc-faction/1"`), one per kind: `Traders`, `Mercenaries`,
  `Raiders` (the Bandit style's), `Militia` (a planet's holder), `Salvagers`, plus `Player Crew` (section 9) and
  `Test Hostile` (what "Fights everyone" means today).
- `Crew/Jobs.json`: each job's methods (the steps of section 3), its station kinds, the stations a goal needs manned.
- `Crew/Names.json`: crew names per faction (ASCII).

A faction file holds:

| Key | What |
| --- | --- |
| `name`, `about`, `styles` | Its name, a line for the look line, the vehicle kit styles its craft are made in (`Bandit`, `IT`...; the style is only a look) |
| `stance` | To players by default: `hostile`, `wary` (fights only who shot first), `neutral`; to each other faction kind |
| `engage` | Sight range, the range band it fights at (or the brief's `Engage`), whether it chases, whether it fights on foot |
| `crew` | Crew per body class (min, max, spares), skill range, share of robots, guns carried |
| `morale` | Start, what lowers it (crew lost, hull lost, out of ammunition, outgunned), what raises it (kills, allies near) |
| `flee` | Hull and morale below which the captain's Flee wins |
| `boarding` | Weights per boarding action (section 8), the morale at which Surrender is allowed, the desperation at which Scuttle is |
| `scuttle` | `never`, `desperate`, `always` when taken; its timer (s), how long arming takes, which points it prefers |
| `capture` | What surrendered crew may be (released, put off, recruited) and what taking a ship gives (standing changes) |
| `needs`, `wants` | Data only for now (NPCs have needs and wants; currency is open): what a trader buys and sells, what a mercenary is paid with. Read by nothing until trade exists |

**Standing** (how each NPC faction sees each player faction) goes in the factions table: `Factions.StoryNames` and
`StoryStanding` (16 kinds x 32 player factions) are already there and already sent whole with `Msg.Factions`; rename them
`NpcNames`, `NpcStanding` and fill them from `Crew/Factions` when the story is off (verify the wire first: if they are
sent as now, no protocol change). A deed (attack a faction's body, kill its crew, take its ship) moves standing; `wary`
turns `hostile` below 0.

**Who owns a faction body.** Today an NPC construct is owner 255 (nobody). Add `Construct.NpcFaction` (a byte, 0: none),
server-side, saved in the crew block (section 12). Its owner stays 255, so every rule about players' ownership is
unchanged. Karl's two settings map onto it: Fights strangers is the `Mercenaries` kind with the ship owner's faction
friendly, Fights everyone is `Test Hostile`; the stabilizer setting stays as a creative shortcut that crews the ship.

## 8. Boarding: what a crew does when you come aboard

**Triggers** (any one, read by the captain at its next think, at once on the first): a hostile player inside the body's
frame (`RideBody` is the body) or seated in it; a breach (a door or hull part between outside and a crewed room destroyed
while a hostile is within 30 m); the body clamped to a hostile dock; its helm taken by a hostile.

**The actions.** The captain scores each by the faction's weights against its state: boarders' strength (count, guns,
health) against defenders', hull and works health, whether the ship can still fly, morale, allies within 3 km. Several
run at once (one per group of crew); the set changes as the fight goes.

| Action | What the crew does | How a player stops it |
| --- | --- | --- |
| **Hold stations** | Pilot keeps flying, evades and tries to throw boarders off (rolls, burns); gunners keep firing at what is outside | Kill the pilot first: the ship stops |
| **Repel** | Guards and spare crew take their guns, hold choke points between the breach and the bridge, engine room and magazine (interior rooms, CrewNav's narrowest edges) | Fight; grenades; come in by another way |
| **Lock down** | Close and lock doors on the way to vital rooms, stop lifts | Cut through, blow the door |
| **Call for help** | Radio: the same faction's crewed bodies within 3 km turn to come (their captain's Follow) | Kill the radio part or jam it (if jamming exists), or be quick |
| **Dump** | Eject cargo (open the hold, push crates out), wipe a route computer: denies the prize | Reach the hold first |
| **Flee** | Burn away at full throttle with boarders aboard | Kill the pilot, wreck the drive works |
| **Abandon** | Crew run for exits (escape pods if fitted, else jump; leaving keeps momentum); the ship is left a derelict, its guns silent | Let them go: the prize is whole |
| **Surrender** | Hands up, guns dropped, every station left; the look line says "surrendered" | Accept: stop shooting (shooting a surrendered crew costs standing) |
| **Scuttle** | Below | Below |

**Scuttle**, when desperate (the faction allows it, the fight is lost by its own reckoning, and it is not allowed to
surrender or chose not to):

1. **Who and where.** The nearest living engineer (else anyone) goes to a scuttle point it can reach on CrewNav. Points,
   by the faction's preference: a magazine or ordnance store (cook-off), a fuel tank bank, the reactor, the drive works.
   A point that is destroyed, or that no living crew member can reach, is off the list. With no point left, no scuttle.
2. **Arming** takes 8 s (faction data) standing at the point: a bodied member there, interruptible by killing or
   stunning them, or damaging the point. Players aboard hear a klaxon and see "Scuttle charge arming: Magazine" on
   their look line from anywhere aboard.
3. **The countdown** (faction data, 30 to 90 s, default 45 s) shows to everyone aboard and within 200 m. At zero the point
   goes up as its own kind does (the existing cook-off, fuel fire or a blast sized by the point), with what it chains to.
   The ship may survive in pieces: what is left is salvage.
4. **Disarming:** at the point, the attach tool's Fire held 3 s (the same act as seeing to a breakdown warning), or
   destroying the charge's part before zero (it then burns, not blasts). Either cancels it; the crew may try again
   elsewhere if they can.

**How the player chooses** is everything they can already do, because the brain only uses what exists: shooting the pilot
stops the ship; wrecking the drive works or the reactor stops it for good; killing the engineer (through a window, from a
boarding position) takes scuttling and repair away; destroying the magazine from outside removes the biggest scuttle point
(and blows it now, which is its own choice); taking the guns out first makes boarding safe. A small, cheap enemy is
easiest to blow up; a valuable one rewards care.

**What capture gives.** A ship is taken when no hostile crew who isn't surrendered is aboard and a player has sat at its
helm for 10 s: its `NpcFaction` goes to 0 and its owner to that player (the existing hand-over, as a faction gift does).
The taker gets the ship and everything aboard (the brief's `Loot` and `Stowed`), the parts to salvage, and the crew's
fate to choose (section 16, question 6). Standing falls with that faction and rises with its enemies.

## 9. Your own crew

- **Robot Shells first** (SURVIVAL.md: robot shells come early; they are parts a player builds, so no currency or hiring is
  needed). A Robot Shell that no player wears, standing in a Charging Cradle aboard, can be put on the owner's roster with
  the configure tool or the Crew window. It is then a crew member of kind robot under the `Player Crew` rules: never
  surrenders, never scuttles unless ordered, never refuses an order. A player who wears it (the chair) takes it over and
  it stands back on the roster when they leave. Human crew (hired from traders) come with currency, later.
- **The Crew window** (a key to choose, or a Crew Console part on the bridge): the roster (name, job, station, health,
  what they are doing), drag a member onto a station, and orders: **Man stations**, **Hold fire**, **Fire at will**,
  **Defend** (guards to choke points), **Follow me**, **Go to** (a station or a spot), **Stand down**. Owner and faction
  members only.
- **Your crew obey you, not rule 1's gate.** A player's own body can be driven by its owner, its logic, its autopilot or
  its crew; the crew are one more way, never a requirement. A crewed own ship's pilot can fly `GameNav` orders and the
  route orders of linked logistics (6.4) from the helm seat; a Route Computer alone still flies a route as designed.
- **Acting at a distance** (SURVIVAL.md idea): later, a Crew Console's orders through a ship's antenna at range; and the
  fleet room's commands become captain goals for each crewed ship.

## 10. The horde, spawns, aircraft and convoys

- **The mass horde stays its own system** (`Source/Game/Horde/`): its tiers do what this design's LOD does, for a crowd.
  Crew treat zombies as threats (the Sandbox's `AreEnemies`); gunners fire at individuals; a crowd near a parked convoy
  makes its captain's Flee or Engage win. Nothing in the horde changes.
- **The Spawn tab** keeps its on-foot bots (the arena `BotBrain` stays for that and for the archived modes, untouched).
  It gains "crewed vehicle" (a faction vehicle spawned with its crew in its seats) and "crew of a faction" (on-foot crew
  with the Guard or Patrol job). The Bot Spawner part gets a faction setting later.
- **Aircraft spawns** seat a pilot crew member: `AircraftPilot` becomes the air `HelmDriver`, its orders the captain's
  goals; `MaxPilots` folds into the crew budget.
- **Convoys** spawn with crew per the brief's `Seats[]` (driver, gunners, spares in the cab or bed); the leader's captain
  takes a path, the others Follow at their `ConvoyIndex` and `Lane`; an escort's air pilot follows the leader.
- **Outposts and bases** (faction bases are designed function first, creative spawn only for now): their guns are
  stations too (rule 1: a base turret needs a gunner in it), their guards Guard and Patrol on foot.

## 11. Networking

The brain runs only on the server, as Karl's crew and the pilots do now. What clients see:

- Bodied crew are bot `Player`s: their movement, seats, animation and names go as any bot's do today (the roster
  broadcast for names). Their guns turn through the existing poses. Nothing new for that.
- New on the wire, each in the phase that needs it, each with a `ProtocolVersion` bump and a line in its history, new
  messages at the end of `Msg`: a crew notice (`CActKind` style: surrendered, scuttle arming, countdown, disarmed) and a
  member's job and faction for the look line (phase 3 or 5); the Crew window's roster to its owner's peers (phase 6).
- Seated and asleep crew, captain state, contacts and plans are never sent.

## 12. Saves

- A **crew block** at the end of a body's bytes (after the brief's, as the brief went after the tracks: WORLD-SAVES.md),
  versioned, skipped with a log line if it doesn't read, never the body: `NpcFaction`, the roster (each member's id,
  faction, kind, skill, health, job, station by part tag, gun), morale, a scuttle countdown if one runs. Bodied members
  are written as records. A Robot Shell on a player's roster keeps its link in the same block.
- Standing goes with the factions table, which the world already saves.
- No new save file: nothing for `SavePaths`, the package list or MIGRATING.md. Old saves load with no crews, except that a
  Flight Stabilizer set to a crew gets a fresh one (as today).

## 13. Performance

- **Budget:** 200 crew in a world, 96 bodied, the crew system at most 1.5 ms a tick on the 2-core box with 20 crewed
  ships in sight; a steady tick allocates nothing. Measure in phase 1 with the biggest crewed ship lying on a slope.
- **Bounded per body:** stations scanned once per rebuild, the station plan re-run only on change, contacts refreshed
  one body per tick round robin, CrewNav built off the tick in slices and patched on a part's loss.
- **Think rates** by tier (section 4); a captain far away thinks every 5 s; an asleep body costs nothing.
- **Fixed arrays** (rosters of 32, contacts of 32 per body, a pool of minds); the bodied bots' cost counted with players.
- **The check:** extend `--ship-ai-check` into `--crew-check [BLUEPRINT] [--crew N] [--target NAME] [--board]`: seats a
  crew, fights or boards, prints the crew system's cost per tick and its allocations. A dev tool, not a self-test section.

## 14. Reuse, and what is not touched

**Reused:** Karl's `ShipAi` (moved into drivers, tuning unchanged); `AircraftPilot`; the brief; the arena bots' path
follower, unstick, sight and aim (`BotFollow`, `BotUnstick`, `BotCombat`) ported, their files left alone; the bot `Player`
and its ids; players' seat and act paths; breakdown state; the construct frame; the building check's reach walker; the
interior planner's rooms; the factions table's NPC standing arrays; the story HTN's shape as a pattern only.

**Not touched:** the archived arena bots and modes (`Bots*`, `BotNav*`, squads, vantage), Zombies and the horde, the
story's people, HTN and factions (`Source/Story/`, `Story/`), the production system (TwitchTheSniper's; haulers and crane
operators only press what a player would), maps and terrain (another developer's).

## 15. Phases (one builder each, the whole phase; a WIP `[skip ci]` after each step)

Value lands early: phase 1 alone gives rule 1 on ships and aircraft with bodies you can shoot.

| Phase | What | Done when |
| --- | --- | --- |
| **1. Crew in seats** | `Source/Crew/`: roster, stations scan, the rule-1 gate, LOD tiers, bodied bot `Player`s that sit (phase 0: can a bot sit and ride?), spares in couches who take an empty seat (a timed walk first, no CrewNav). `ShipAi` split into helm and gun drivers behind the gate; aircraft pilots seated. The stabilizer setting crews the ship. Crew block in saves. `--crew-check` | The crewed Zero Day fights as before in `--crew-check`; shooting its pilot stops it, a spare takes the helm; a gunner shot silences its turret; 20 crewed ships under the budget; a host and a joined client see the crew |
| **2. Ground crews** | Ground `HelmDriver` from the brief (paths over the ground, Follow in convoys, keep-away side, engage band), walkers through the Driving Module; gunners on ground vehicles (VEHICLE-WEAPONS.md's queued "gunners"); convoys and faction vehicles spawn crewed | A convoy drives in formation and fights; killing a driver stops that truck |
| **3. Factions and the captain** | `Crew/Factions`, `Crew/Jobs.json`, `NpcFaction`, standing; contacts per body; captain utility (Engage, Hold, Patrol, Follow, Route, Land, Flee); look line with name, faction, job (protocol bump) | Traders flee, mercenaries fight, wary ones only answer fire; standing moves |
| **4. Inside the ship** | CrewNav (building check walker, interior hints, patching), bodied walking with doors and lifts; engineers see to breakdowns and mend, loaders carry reloads; breakdowns run on NPC ships once engineers do (Karl) | A frigate's spare walks from its couch to the helm; an NPC ship's breakdown is mended by its engineer, not if he is dead |
| **5. Boarding** | Triggers, the action set, guards, lock down, call for help, abandon, surrender, scuttle (points, arming, countdown, disarming), capture and its hand-over, crew notices (protocol bump) | Boarding a mercenary frigate ends in each of: fight won, surrender, scuttle disarmed, scuttle gone off; killing the engineer from outside removes scuttle |
| **6. Your own crew** | Robot Shells on the roster, the Crew window and its orders, own pilots flying nav and route orders | A player crews their ship's guns with two shells and flies it; the shells fight off a raider |
| **7. Work crews** | Haulers inside a body, crane operators, spotters on capital guns, builder drones (each may want its own short design first) | Each job works one station of its kind end to end |

## 16. Open questions for the user (recommendation first)

1. **Faction guns with no gunner (base turrets, NPC sentries).** Recommend: no exception, every faction gun needs a crew
   member at its station, as the rule says; players' sentries and later auto-targeting parts stay players' only. Or: allow
   faction "automated turrets" as a rarer, weaker kind.
2. **An empty helm in the air.** Recommend: a ship or aircraft whose stabilizer can hover holds where it is (its stabilizer's
   hold, as "nothing to fight" does now), one that can't glides down; in space it coasts with thrust 0. Or: everything
   falls (dramatic, but a 300 t ship dropped on your base by your own sniper).
3. **Crew hit from far away.** Recommend: a seated member dies with its station part and takes blast damage there; only
   crew within 300 m of a player are real bodies. Or: promote crew on any body under fire, at a body cost.
4. **Your first crew.** Recommend: Robot Shells (built, no currency). Or: hired humans first, which waits on currency.
5. **When a ship is taken.** Recommend: no un-surrendered hostile crew aboard and a player at the helm for 10 s. Or: the
   helm alone (quicker, easier to exploit).
6. **Prisoners.** Recommend: released (they walk off or go to a pod) or put off at a dock; recruiting comes with currency.
   Or: they stay aboard as dead weight.
7. **Scuttle numbers.** Recommend: 8 s to arm, 45 s countdown (30 to 90 by faction), 3 s to disarm. Or shorter, for tension.
8. **Crew budget.** Recommend 200 crew a world, 96 bodied, sharing the spawned bots' ids (the Spawn tab's cap falls while
   crews are bodied). Or: a separate, lower cap for crew.
9. **Far seats.** Recommend: nothing drawn for seated crew past 300 m. Or: a cheap seated figure in an occupied seat (a
   bit per seat on the wire).
10. **Where the faction files live.** Recommend `Crew/Factions/` beside `Constructs/`. Or `Constructs/Factions/`.

## 17. To agree with Karl (he owns ships and the crew)

- **Rule 1 replaces the bodiless crew** (proposal): his crew's flying and gunnery move unchanged into drivers, but a
  driver presses keys only with a crew member in that seat; the stabilizer setting now seats a crew. Killing the pilot
  stops the ship.
- **Breakdowns on NPC ships** once engineers exist (today `GameSandboxBreakdowns` skips crewed ships).
- **`--ship-ai-check` becomes `--crew-check`**, seating a crew first; same lines and figures.
- **Scuttle points on his ships:** his answer 5 made a broken Drive Core only stop. Propose: scuttling uses the magazine,
  the fuel and the reactor, never the Drive Core; or a Scuttle Charge fitting in the crew fittings.
- **Crew fittings as stations:** the Crash Couch and Charging Cradle as where spare crew wait, the Gunnery Console as a
  gunner's station, the Galley for morale later.

## The user's answers (2026-11-16)

These override the text above where they differ.

1. **Every faction gun needs crew,** no exceptions (fixed base guns and small turrets too). Auto-targeting parts stay a later, player-built option.
2. **Empty helm:** the ship keeps its last commanded input for a short while (a few seconds), and if another pilot aboard is free, that crew member goes to the seat and takes the helm if they can reach it. Only with no replacement does the ship fall back to holding (if it can hover) or gliding down.
3. **Hits on far crew:** a crew record goes down with its station part (killed or hurt when the part is destroyed or badly hit, and by damage near it).
4. **First own crew: hired NPC crew** (not Robot Shells first). Hiring needs a way to pay or recruit before currency is decided: propose one (for example recruiting surrendered or rescued crew, or a placeholder cost in materials) as an open question, and keep Robot Shells as the next kind.
5. **Capture: all crew gone.** A boarded ship becomes the player's once every hostile crew member aboard is dead, fled or surrendered (not a helm timer).
6. **Prisoners: all three options.** The player can release surrendered crew or drop them at a dock, hire them (ties to answer 4), or hold them as prisoners (a brig part; ransom or trade later, once trade exists).
7. **Scuttle: longer and more tense.** 15 s to arm, a 90 s countdown, 5 s for a player to disarm at the scuttle point. Killing the engineer first still prevents it.
8. **Scale: thousands of crew across all ships.** The budget is no longer 200: design for thousands of crew in the world at once. Only crew near players are bodies (keep the body cap tight); everyone else is a record, ships far from players think in coarse batches on a slow clock (sliced across ticks, a fixed cost per tick whatever the count), and sleeping ships cost nothing. Data layout for that many (structs in arrays, no per-crew objects or allocation on the hot path). Measure at several thousand before calling a phase done.
9. **Far crew are drawn as cheap stand-ins:** simple low-detail figures at their stations, no animation or physics, batched in one draw, so a ship flying past or seen through a scope shows its crew.
10. **Data lives in a new top-level `Crew/` folder:** `Crew/Factions/*.json`, `Crew/Jobs.json`, moddable like the rest.

The "To agree with Karl" list stands: raise it with him before phase 1 changes his crew.

**Karl agreed (2026-11-16, via the user)** to the "To agree with Karl" list: seated crew replace his bodiless crew, NPC ships get breakdowns once engineers exist, `--ship-ai-check` becomes `--crew-check`, scuttling as the design proposes, and his crew fittings become stations.

**Hiring and credits (user, 2026-11-16):** hired NPC crew are paid in a placeholder currency, **credits**, added to the game for now (name may change). In Creative the player has unlimited credits. Hiring, wages and prisoners for ransom use credits; the trade design comes later.
