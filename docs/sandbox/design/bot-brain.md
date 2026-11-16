# The bot brain: one AI for crews, pilots, NPCs and your own bots (design)

Design only (2026-11-16). No code changed, nothing built, nothing run. Written for builders who each take a whole phase
(section 15), a WIP commit after each step. Section 16 holds the open questions for the user (recommendation first), section
17 what must be agreed with Karl before phase 1 touches his ship crew. An answer there overrides the text above it.

The user's ask (2026-11-16): one design covering all AI, bots and crew in Sandbox, so the separate systems become one.

Sources read (excerpts): `ConstructShipAi.cs`, `GameSandboxShipAi.cs`, torpedo-frigate.md "The crew", `GameSandboxAircraft.cs`
and `AircraftPilot.cs` (orders), `GameSandboxSpawns.cs` (header, caps), `GameSandboxBreakdowns.cs` (header),
`GameRobotShell.cs` (header), `GameSandboxFactions.cs` and `Factions.cs` (headers, owner bytes), `GameSandboxVehicles.cs`
(header), `Bots.cs`, `BotMove.cs`, `BotCombat.cs` (signatures), `StoryHtn.cs`, `StoryAgents.cs` (headers), `Story/Factions/*.json`
(head), docs/vehiclekit/SPAWNING.md "Behaviour data", MASS-HORDE.md "The tiers", SURVIVAL.md, INTERIORS.md (head),
VEHICLE-WEAPONS.md (gunners note), ship-works.md section 7, construct-frame-notes.md, linked-logistics.md 6.4-6.5,
`CrewFittingsParts.cs` (header), git log -12.

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

| Thing | What it does | Body? | Where |
| --- | --- | --- | --- |
| Ship AI crew (Karl, 2026-11-15) | A Flight Stabilizer setting (Off, Fights strangers, Fights everyone). Works the ship's own seats by writing their keys and aim (`ShipAi.Seat`): pilot through mouse aim, Shift/Ctrl, W/S, Q/E; gunners lay and fire turrets within 4 degrees (2.5 fixed); Buttons wired to Ordnance Racks every 8 s; flares. Target: nearest living player in 2.5 km. A player at the helm stands it down. Not saved (the setting is). | None: seats get `CPart.Occupied` without a sitter | `ConstructShipAi.cs`, `GameSandboxShipAi.cs`, `ShipAiCheck.cs` |
| Aircraft bot pilots | Hold, Patrol, Goto, Return, Land through the stabilizer's autopilot; at most 32 (`MaxPilots`); air escorts for convoys | None | `GameSandboxAircraft.cs`, `AircraftPilot.cs` |
| Spawned bots | The arena `BotBrain` on foot (difficulty, gun, side), from the Spawn tab and the Bot Spawner part; each one is a `Player` with an id above the people's seats (`SpawnBotCapMax = MaxPlayers 224 - MaxPeople`) | Yes, a full `Player` | `GameSandboxSpawns.cs`, `Bots*.cs`, `BotNav*.cs` |
| Faction ground vehicles and convoys | Generated and set down with a **brief** (guns and arcs, seats and what each controls, weak points, keep-away side, steering, entries, loot). Nothing reads the brief; nothing drives them | None | `VehicleSpawn`, `VehicleBrief*.cs`, `VehicleConvoys.cs` |
| Mass horde | Zombies in three tiers (individuals, crowd particles, mass counts) | Tiered | `Source/Game/Horde/` |
| Robot shells | A player in a Shell Control Chair wakes in a Robot Shell and plays it as themselves | The player's | `GameRobotShell.cs`, `ConstructShell*.cs` |
| Breakdowns | Every crewed player ship's drive parts warn, then fail; mended at the part or the Engineer's Panel. Skips ships with an AI crew | - | `GameSandboxBreakdowns.cs` |
| Sentries | A gun with a proximity sensor wired in aims and fires by itself | - | weapons.md "A sentry" |
| Story people (archived) | Needs fall, goals scored by utility, the best planned by a bounded, allocation-free HTN over verbs; a few think per tick | Had bodies | `StoryAgents.cs`, `StoryHtn.cs` |
| Factions | Player factions (32, keyed by owner byte, a stance table); the story's factions are archived but the table still has `StoryNames`, `StoryStanding` (16 x 32) | - | `Factions.cs` |
| Crew fittings | Crash Couch (respawn bunk), Flight Couch, Gunnery Console, Galley Coffee Station, Robot Charging Cradle on every ship | - | `CrewFittingsParts.cs` |
| The construct frame | One frame per moving construct (2 m round its bounds), bodies spawned aboard take its motion, leaving keeps momentum; bots' plain ride rides it | - | `ConstructRide*.cs` |
| Linked logistics (design) | Route orders (2 to 4 stops, Dock/Undock), the Dispatcher sends registered ships | - | linked-logistics.md 6.4, 6.5 |

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
- **Look line** on any crew member: name, faction, job, what they are doing ("gunner, Turret 2: firing"; "engineer:
  arming the scuttle charge, 23 s").

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

| Job | Station | What the driver does | Reads | Phase |
| --- | --- | --- | --- | --- |
| **Pilot** | Helm (drive seat / stabilizer seat) | Ships: Karl's flying. Aircraft: `AircraftPilot`'s orders. Ground: follow a path, a leader, keep the brief's `KeepAway` side off the enemy, hold `Engage` range for its gunners, brake to stop. Walkers and giants: the same through the Driving Module's keys, slower turn caps | Brief (`Steer`, `TurnRadius`, `TopSpeed`, `Climb`, `TipSide`), contacts, orders | 1 (ships, air), 2 (ground) |
| **Gunner** | A gunner's seat or Gunnery Console | Karl's laying and firing; launch buttons; flares. Ground: the brief's `Clear` bins before firing | Contacts, `CGun`, brief `Guns[]` | 1 |
| **Engineer** | Engineer's Panel, works pieces, fires | Sees to breakdown warnings, mends broken works pieces (same act and cost as a player's), puts out fires; in a boarding, the one who scuttles | `GameSandboxBreakdowns` state, works health | 4 |
| **Loader** | Magazine, racks, autoloader feed | Carries reloads to a rack or feed by hand when a gunner runs low | Ammo counts, brief `Stowed` | 4 |
| **Hauler** | Store, machines, Cargo Dock | Carries crates and pieces inside a body ("players and bots carry things by hand", INTERIORS.md), works a dock's exchange | Logistics network's moves | 7 |
| **Crane operator** | Loader Arm, Bridge Crane seats | Pick and put by the lifters' points | Lifters' pick and put points | 7 |
| **Guard / marine** | A room or choke point | Holds it with a hand gun; repels boarders | Contacts aboard, interior rooms | 5 |
| **Spotter** | Beside a capital gun | Shares a target's range and lead with its gunners (tighter bearing tolerance) | Contacts | 7 |
| **Builder drone** | A building station's bay | Works a station's job beside its arms (later, its own design) | Building station jobs | 7 |
| **Passenger / rest** | Couch, cradle, galley | Spare crew wait here and fill a station that empties | - | 1 |

Spare crew are what make rule 1 fair: a ship with 6 crew and 4 stations survives losing a pilot once; a two-seat buggy
does not.
