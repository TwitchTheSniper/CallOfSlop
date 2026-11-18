# Bot brain: builder's notes

Working notes for docs/sandbox/design/bot-brain.md, kept current after every step so a successor can finish. Branch `claude/bot-brain` (phase 1, merged), then `claude/bot-brain-2` (phases 2, 3, 5, 6, part of 4: merged), then `claude/bot-brain-3` (phase 4's rest, phase 7).

## Done (phase 1: crew in seats)

- `Source/Crew/CrewWorld.cs`: the crews. Members are struct-of-arrays records (uid, body, job, state, health, skill, station tag
  and index, body player, walk end, name); a `CrewShip` per crewed body (pooled) holds its roster and stations (helm from
  `ConstructFlight.SeatFor`, else the drive seat; each gun's seat; every other seat a rest station). Stations follow rebuilds by
  part tag; a member whose station part is destroyed or broken dies with it, one whose part is shot about is hurt by the share
  it lost (`HitShare`).
- The rule-1 gate is in `ShipAi` (Karl's flying and gunnery unchanged): it writes a seat's keys only while `CrewShip.Manned`; the
  helm keeps its last input (fire off) for `HelmGraceTicks` (3 s) after its pilot is lost; racks and flares are the pilot's.
  With nobody at the helm the Flight Stabilizer holds as it does with no sitter (rotorcraft and ships hold, planes glide).
- Spares (one per couch, at least one) walk to an empty helm first, then gun seats; with no spare a gunner leaves their gun for
  the helm. The walk is a timer (`WalkSpeed`, at least 2 s) until CrewNav (phase 4).
- Tiers: within 300 m of a person (out at 380) members in seats get bot `Player` bodies (`MaxBodied` 32, sharing the spawned
  bots' cap and ids); within 3 km (out at 3.2 km) the body has a driver (at most `MaxDrivers` 64, pooled), thinking every tick
  bodied, every other tick seated (`ShipAi.Replay` holds the keys between); further, asleep: nothing runs.
- Fixed cost per tick: `FindPerTick` 64 world bodies looked at for a new crew (the stabilizer's AI crew setting), `ShipsPerTick`
  48 crewed bodies thought about, the bodied members' deaths, the drivers.
- `Game/GameCrew.cs` (the host: bodies, targets with a contact kept between looks every 0.5 s, the stand-ins' message),
  `Game/GameCrewDraw.cs` (stand-ins: four boxes per seated crew member with no body, the box batch, 2.5 km, 600 a frame).
  `Player.CrewSlot`: a crew body skips the bot brain, never respawns, and its seat's keys come from its crew.
- Aircraft pilots are seated crew: `PilotHeld` gates `AircraftPilot` (grace, then the autopilot is let go once).
- Saves: the CREW tagged block, last in a body's bytes (`CrewWorld.SaveBytes`/`Load`, `Construct.CrewLoad` until picked up).
- Protocol 329: `Msg.CrewSeats` (u16 body, u8 n, n x u16 seat part).
- Data: `Crew/Names.json` (`CrewNames`).
- `--crew-check` replaces `--ship-ai-check` (Source/Crew/CrewCheck.cs): fights, then kills the pilot (40 s), the gunners (70 s)
  and the rest (90 s); `--crew-scale N` times the crew alone at N members.

## Verified (2026-11-16)

- `--crew-check`: the crewed Zero Day lifts off, closes to 236 m and hits the target; its pilot shot at 40 s, a spare takes the
  helm 2.0 s later; its gunners shot at 70 s, no gunner fires for the next second (spares then take the seats); everyone shot
  at 90 s, it holds (3 m in its last 10 s, 0.1 m/s). The crew's own cost 36 us a tick for one fighting frigate.
- `--crew-check --seconds 0 --crew-scale 5000 --crew-scale-ship "Zero Day Frigate"`: 5027 crew on 209 frigates, 20 awake within
  2 km: 47 us a tick on average, worst 0.95 ms, 0 bytes in 600 ticks; records 8192 slots (about 344 KB). On 209 helicopters:
  21 us average, worst 48 us.
- A window run (`--window-shot --blueprint "Zero Day Frigate (crewed)" --do "...; crew; crewshoot gunner; ..."`): six bodies
  sit in their seats within 300 m; a gunner's body shot through `DamagePlayer` dies, a spare walks over (5 m) and takes the gun;
  the pilot shot, a spare takes the helm and the ship flies and fights on. A joined client sees the six crew bodies.
- Window-shot verbs for this: `crew` (rosters, shots; on a client the bodies and stand-ins it has), `crewshoot pilot|gunner|spare`,
  `crewbodies N` (cap the host's bodies; the rest go back to records and are drawn as stand-ins).

- Saves: a crewed frigate saved after its gunner was shot (`saveworld`) loads with its crew of 5 (`loadworld`).
- A joined client (`--net host` / `join`, `crewbodies 2`): 2 crew bodies among its players and the other 4 as stand-ins.
- Self-test sections by name: Sandbox spawns, Frame allocations, Sandbox body sync, World links: all pass.
- Review fixes (2026-11-16): a player who uses a seat a crew body is in always gets it (`ConstructEdits.CrewYield`: the member
  stands up and walks to a free couch); a person at the helm releases the crew's forced mouse aim; a body's wounds go back to
  its record when it goes; crew at the controls don't count as a person taking over (`ConstructWorld.HumanDriver`, in GameNav,
  linked routes); bodies are made two a tick. `--crew-check` after them: worst crew tick 1.25 ms (in Karl's driving, its rays),
  worst thinking 86 us; the earlier 5.7 ms spike didn't come back (taken for a one-off JIT or GC).

## Done (phase 2: ground crews)

- `Source/Crew/CrewGround.cs` (a partial of Karl's `ShipAi`): with no Flight Stabilizer the helm is the drive seat and it is driven by keys
  (W, S, A, D, Space). A convoy follower keeps the place it was set down in, in its leader's frame (`brief.Leader`, `ConvoyIndex`, `Lane`);
  the leader drives out and back 600 m along the way it was set down facing; a lone vehicle holds, and with a target closes to the brief's
  `Engage` (80 m without one) and holds there, its `KeepAway` side turned off. A body in its way (a ray ahead every 10 ticks) is steered round;
  throttle open and not moving for 3 s, it backs off 2 s. Gunners are Karl's gunnery unchanged.
- Rule 1 on the ground: no member at the helm (after the grace) and its seat free, the drive gets Space (throttle 0, brakes on).
- `Construct.CrewDrives` / `Construct.Driven`: a seated (bodiless) crew member at the drive seat drives as a sitter would; every
  `c.Driver != 255` read in the drive code (Physics, Power, Drive, Thrust, Walk) now reads `c.Driven`. Server only, never saved or sent.
- A nobody's vehicle set down by the vehicle spawner (`GameSandboxVehicles.Brief`) gets a crew (`Game.CrewFactionVehicle`). The parts menu's
  convoy is now a nobody's crewed convoy (its asker still hears of each member); a single faction vehicle from the menu stays the asker's.
- `--crew-check --crew-convoy "A,B,C,D" [--seconds S]`; window verb `crewconvoy STYLE N`.

Verified (2026-11-16): `--crew-convoy "Bandit Gun Wagon 1,Bandit Buggy 1,Bandit Car 1,Bandit Monster Truck 1"`: the leader drives 400 m in
60 s, followers 14 m off their places on average (the two behind the stopped one steer round it and catch up), 518 shots at the target;
the second's driver shot, its gunner takes the wheel 2 s later; everyone aboard shot, it stops (0.0 m/s). Crew cost 39 us a tick, worst
4.1 ms once (driving, at 17 s: not seen again; taken for JIT), 0 bytes. `--crew-scale 5000` on Bandit Buggy 1: 92 us a tick, worst 0.86 ms,
0 bytes. A window run (`crewconvoy Bandit 3`): three generated vehicles crewed, eight bodies in seats, the convoy drives and fires.

Unverified: walkers through the Driving Module (the same keys); a convoy's own gunners hit their own convoy (no friends yet: phase 3).

## Done (phase 3: factions and the captain)

- `Crew/Factions/*.json` ("call-of-slop-npc-faction/1", `Source/Crew/CrewFactions.cs`): Traders, Mercenaries, Raiders, Militia,
  Salvagers, Player Crew, Test Hostile; numbered in name order (`CrewFactions.Find` by name: saves keep the name). Keys: name, about,
  styles, stance (players; factions), engage (sight, range, chase, onFoot), crew (spares, skill), morale, flee, boarding (weights per
  `BoardAct`, surrenderMorale, scuttleDesperation), scuttle (when, arm 15, countdown 90, disarm 5, points), capture (release, drop, hire,
  brig), hire (cost, wage), needs/wants (read by nothing). A bad file is REFUSED with its reason.
- `Construct.NpcFaction` (server, saved with the crew block, v2: the kind's name, morale, losses). Who gets one: a nobody's generated
  vehicle by its style (`CrewFactions.ForStyle`, else Raiders), a nobody's aircraft the same, a stabilizer set to Fights everyone is
  Test Hostile, to Fights strangers on a nobody's ship Mercenaries; an owned ship's crew has none (the old rule: spares its faction).
- The captain (`CrewWorld.Captain`, 1 Hz bodied, every 5 s seated): who hit the body (`Construct.LastHitBy`, set in `DamagePart` from
  `DamageBy`) becomes a foe and a deed; hull share and losses make morale; goal Engage with a contact, Flee under the faction's hull or
  morale line, else Hold (the drivers hold, patrol or follow). Flee: ships turn their nose away at full ahead (no racks), ground vehicles
  drive away at speed, convoy place or not.
- Hostility (`Game.Fights`): a foe, or standing below 0. Standing (`Game/GameCrewFactions.cs`) sits in the factions table's NPC rows
  (`StoryNames` filled from Crew/Factions while the story is off; `StoryStanding` per player faction, `PlayerStanding` for someone in
  none), already sent by `Msg.Factions`, so the Factions window shows "How the world's factions see you". Starts: hostile -50, wary 0,
  neutral 20. A hit on a body -5 (once per captain look), on a crew body -3; the kind's enemies +1. Crews also fight other kinds'
  awake crewed bodies they are hostile to (`Target`'s look).
- Saves: the world's Crew section (`WorldSaveWriter.CrewTag`, "CREW"): each record's credits and own standings (by kind name), then the
  player factions' standings. No new save file.
- Protocol 332: `Msg.CrewSeats` gains u8 faction kind and u8 crew alive; the look line adds "(Militia, crew of 3)"; crew bodies are
  named "[Militia] Name".
- Tools: `--crew-check --crew-faction NAME` (with `--crew-convoy` too); window verbs `crewhit` (a server shortcut: 1 hp off a crewed
  body as this player's shot), `npcstanding`.

Verified (2026-11-16): `--crew-check --seconds 30 --crew-faction Traders`: the frigate turns and runs (172-179 degrees off its nose,
1107 m to 1624 m), no shots. `--crew-faction Mercenaries`: fights as before (2762 shots, every act OK, 32 us a tick, 0 bytes). Window
run, `crewconvoy Militia 2`: 25 s side by side, 0 shots; `crewhit`: "Militia now fight player 0 (standing -5)", both captains fight, 102
shots in 6 s.

Unverified: standing after a save and load; the Factions window's rows (no picture); two hostile NPC crews meeting.

## Done (phase 4, part: engineers, loaders, breakdowns on NPC ships)

- Jobs `Engineer` and `Loader`, stations `Works` (the Engineer's Panel, else the first drive works piece, else a jet on a stabilised craft)
  and `Magazine` (the ammo box or magazine with the most room, on a body with guns). They aren't seats: their members are records, never
  bodies, and aren't drawn. A standing station's member dies with it only when it is destroyed (health 0), not when it breaks down.
  Fill takes them after the guns; an engineer or loader leaves for the helm only when no spare is left.
- `Game/GameCrewWork.cs`: an NPC crew's ship (faction not 0) breaks down on its own clock while its crew is awake (Survival, as a player's
  ship); `GameSandboxBreakdowns.BreakdownShip` now skips only a player's ship crewed by its own stabilizer setting. The engineer walks
  (distance x 1.4 / walk speed) and sees to a warning (3 s), repairs a part broken down (12 s), mends a shot-out works piece (20 s), free.
  Nobody at the Works station: nothing is done, and a spare walks there to take the job. A loader carries up to 200 rounds every 8 s from
  the fullest source to the emptiest ammo box under a third full.
- Window verbs `crewbreak [fail]` and `crewkill JOB` (server shortcuts).

Verified (2026-11-16, window run on the crewed Zero Day): a warning on the port drive core seen to by its engineer 11 s later; a
starboard drive core broken down, repaired 21 s later; the engineer killed, a spare walked to the engine room and took the job, and went
to the next broken-down lift jet.

Not done (phase 4's rest, for a successor): **CrewNav**, the walk graph inside a body (the design's section 5: deck spots, doors, lifts,
the building check's walker in the rest frame, built in slices, patched on a part's loss), bodied members walking it, and so stations a
member can't reach. Walks are still timers (straight-line distance) and bodies still move seat to seat by `CrewSit`. Loaders untried in
a run (the Zero Day has no ammo box).

## Done (phase 5: boarding)

- `Game/GameCrewBoard.cs`, run from the captain's look (`ICrewHost.Board`): boarders are players riding the body or sitting in it; each
  becomes a foe (-10 standing). Acts weighed from the faction's `boarding.weights` against boarders/defenders, morale (falls 0.15 x the
  ratio a look), hull and the helm: Hold, Repel (bodied spares on couches stand up, `CrewState.Fighting`, and their bodies' bot brains
  fight: `CrewBodyFights` in Game.cs's tick), Flee, Call for help (once: the same faction's awake crews within 3 km mark the boarder a
  foe), Abandon (the crew goes, a derelict), Surrender (every member `Surrendered`, off their stations: rule 1 stops the ship), Scuttle.
  Lock down and Dump weigh nothing (no locks, no cargo dump yet). Surrender, Abandon and a running scuttle hold once chosen.
- Scuttle: only an engineer (job Engineer, alive, not surrendered: the user's answer 7) walks to the faction's first point kind with a
  whole part (`magazine`: an ammo box with rounds or an ammo store; `fuel`: a fuel tank or tank volume; `reactor`: a big battery), arms
  it (15 s; killed meanwhile, no charge), counts down (90 s, notices every 30 s then 10 s), then the point is shot to nothing (a cook-off
  or fuel blast; else a blast of 8 m, 900). Disarm: the attach tool's Fire at the point (the Mend act; the look line says SCUTTLE CHARGE)
  starts 5 s that need the player alive within 5 m; the point destroyed first, no blast. A disarmed point isn't tried again; another may be.
- Capture: boarders aboard and no member left who isn't dead or surrendered: `CrewShip.Prize`, owner the first boarder (if they may own
  another), `NpcFaction` 0, -20 standing, a notice. Prisoners (`CrewPrisoner`): release (+5), drop (the ship below 1 m/s, +3), hold;
  hire is phase 6. Saved: the crew block's flags bit 4 (a prize) and a job's 0x80 bit (surrendered).
- Notices go by `SandboxHands.Notices` to everyone aboard or within 200 m (no new message). `Msg.CrewSeats` (still 332 on this branch)
  also carries the scuttle point and its stage, for the client's look line.
- Window verbs: `crewfaction NAME` (a test state), `crewdisarm` (a server shortcut: stands the player by the charge and starts the
  disarm through the Mend act's server path), `crewprisoner release|drop|hold|hire`.

Verified (2026-11-16, window runs on the crewed Zero Day, the player sat in a Crash Couch): as Traders, surrender at once (morale 47%),
taken by player 0 with 5 prisoners, one released. As Raiders with gunners and pilot shot: Scuttle; the gunner-turned-spare had become
the engineer... (a spare who walks to the engine room takes the job); armed at the Ammo Crate, went off 90 s later (the crate cooked off);
another run, `crewdisarm`: "player 0 disarmed the scuttle charge", the crew went for another crate.

Unverified: Repel with bodies fighting a boarder (no run with spares on couches and a boarder on foot); a joined client's look line on
a charge; Call for help; Abandon; boarding a ground vehicle.

## Done (phase 6: your own crew, credits)

- Credits (`Game/GameCrewHire.cs`): a balance per player, saved in the world's Crew section with their record (credits, a "start
  given" flag); Creative unlimited; a new person in a Survival world gets 500 once; taking a ship pays 100 + 25 a prisoner. `Msg.Credits`
  (u64 as i64, -1 unlimited) to its owner on change; the HUD's held line shows "Credits: N" and the Faction window's Crew section too.
- Hiring (the user's answers 4 and 6): a prisoner on a ship you took (their kind's `hire.cost`), or (body 0) the nearest member of a
  friendly crew (standing 20 or more, bodied, within 15 m), who joins the ship you're aboard, else your nearest own ship within 60 m
  (an empty Player Crew roster is made for it). A prize's first hire turns its roster into your crew (the rest stay prisoners). Each hand
  keeps the kind it came from (`CrewWorld.Fac`) for wages and standing; every 10 minutes each is paid their kind's wage, unpaid they leave.
- Own crews (`CrewShip.Own`, the Player Crew kind; the body keeps no NPC faction): they man guns, engine room and magazine, never the
  helm; never brake, aim or stand down the ship's own driving (no `CrewAims`, no `GroundStop`, a person at the helm doesn't stand them
  down); they fight foes of the ship, players of a faction at war with the owner's, and NPC crews whose kind's standing with the owner is
  below 0. Boarding acts don't run on them.
- The Crew section (top of the Faction window, `UI/UiCrew.cs`): credits; each own crew and prize: members (name, kind, job, doing,
  health) with Dismiss, Hold fire / Fire at will, and for prisoners Release, Put off, Hire; "Hire a hand nearby". Orders go as
  `CActKind.Crew` (u16 body, u32 uid, u8 `CrewOrder`); rosters as `Msg.CrewRoster` to all (own crews and prizes only). Protocol 332 still.

Verified (2026-11-16): Survival start 500 credits, 500 again after a save and load (not 1000). Creative "unlimited". The crewed Zero
Day as Traders, boarded, surrendered, taken; two prisoners hired: the roster became Player Crew, both walked to the gun seats; the
host's roster view showed 2 at work and 3 prisoners.

Unverified: the Crew section drawn (no picture); a joined client's roster, credits and orders; wages and leaving unpaid; hiring from a
friendly crew nearby; own gunners firing at a hostile.

## Review fixes (2026-11-17)

- Boarders are only those the crew's rules call hostile (`Game.BoardFoe`: `Fights`, a foe or standing below 0), never the ship's owner or
  anyone in the owner's faction; own crews run no boarding acts. A friendly player on a Traders deck is no boarder (standing stays 20).
- A player-owned ship is never captured by killing its crew (`CrewCapture` only for a nobody's ship).
- Credits and NPC standings travel: the landing reads the Crew section with the Players section (`GameSpaceLanding`), and a traveller's
  record from another game takes its Crew section's entry (`WorldReadCrewRecords`, `WorldTravellerCopy`). Hired hands travel in their
  ship's crew block, as before.
- One breakdown step per body per `BdStep` (`BdDue`), whether the riders' look or the NPC crew's work tick steps it.
- Crew bodies' side (`CrewTeam`): a hired crew and an owned ship's Fights strangers crew are friendly bots; a prize's roster hired turns
  its bodies friendly (`CrewTeamsFollow`).
- `CrewHire` checks every refusal (crew not yours, full, credits) before it makes an empty Own roster or turns a prize.
- `CrewFactions.All` and `CrewNames.All` load at a world's start (`CrewFactionsClear`), not in a tick.
- The scuttle charge's look line is cached; the charge follows its part by tag (`CrewShip.ScTag`, `CrewWorld.ScuttleFollow`, on each
  rebuild scan and each scuttle step): gone with a split's other piece, the point is lost and no charge goes off.
- Window verbs: `crewprisoner OP` on a joined client sends the Crew window's order for the first prisoner in its rosters; `crewhit [PLAYER]`.

Verified (2026-11-17): a joined client (two xvfb windows, Proving Ground with `inv survival on`): brought onto a Traders frigate's deck,
not a boarder (Traders 20); as Militia, after `crewhit 1`, boarded, the last crew surrendered, taken by player 1 with 1 prisoner, credits
500 to 625 (bounty); a hire with 125 credits refused ("that costs 200 credits: you have 125") and the prize stayed a prize; with 625,
hired (425 left), the roster turned own and the hand took a gun seat. `--crew-check` OK (40 us a tick, worst 0.47 ms); `--linked-check`
passed; sections Sandbox spawns, Mode switch reset, Construct drivetrain, Standalone powertrain, Large drive, Construct weapons, Seated
riders, Save folder, Packets, Space launch pass after merging main (protocol 332, above main's 331).

Unverified: a landing on another planet with credits (no run); hired bodies beside the player's spawned friendly bots; an owner aboard
their own Fights everyone ship (by reading only); a scuttle point split off; `EngPart` (the engineer's part) is still an index, not a tag.

## Done (phase 4's rest: CrewNav, branch `claude/bot-brain-3`)

- `Source/Crew/CrewNav.cs`: one walk graph per body in its rest frame, only for bodied crews (a person within 300 m) with a game (a tool's
  world has no bodies: none built). Its solid boxes (`SolidBox`, through `SegPose`) on a 0.2 m grid; each column's surfaces are spots, fit
  where a standing body (radius `World.PlayerRadius`, `PlayerHeight`) has room; a part that isn't a stair, ramp or floor (`Walk` none) is
  stepped onto only within a 6 cm lip (BlockPlayer steps onto such a part only with the feet's centre over it); spots within 0.4 m of a
  drop are left out (a centre that drifts over an edge falls). Upright door leaves are left out (the walker opens them: `ConstructDoors.Use`,
  never a locked one); a flat door is a hatch and walked on. Ladders join foot to landing. Connected parts are labelled (`Comp`, `CompSize`:
  a pocket of under 16 spots is no place to start). Built in slices under a CPU budget (`BuildBudgetMs` 0.35 ms a tick: boxes, cells,
  columns, drop edges, ladders, parts), one build at a time, at most 16 graphs kept (oldest out). Keyed by `ShapeOf` (parts' tags, places,
  turns, whole or gone): a rebuild that moved nothing (a breakdown, a repair) keeps the graph; a part lost builds it again (not patched).
- Searches: A* on the graph (4 columns a step up or down, ladders), one at a time, `SearchBudgetMs` 0.3 ms a tick; the way pulled straight
  where a body can walk straight (never past a ladder). Walkers (64, fixed arrays, 48 turning points each): `Steer` gives the keys
  (forward at 75%, 50% near a turn; yaw in the ship's frame and the world's), climbs a ladder at its end, opens a closed upright door ahead
  twice a second, gives up after 3 s with no headway (or at a locked door): then it is put at its station (the timer's way, logged).
- Crew bodies ride the ship's own frame (contraption space) as people do: `Player.FramedRider` (people and crew bodies; spawned bots ride the
  plain way, as before). In the plain ride a walking body crossing a deck seam was pushed out below the deck.
- `CrewWorld`: each station's spot (`StNode`; a seat's feet, a standing station's foot); a member who can't reach a station (another
  connected part) isn't sent (`Reaches`); a bodied member sent to a station stands up at its spot and walks (`WalkBody`), seated or
  standing there on arrival (`Arrived`). Engineers and loaders now have bodies standing at their places (`ICrewHost.Promote` with a spot,
  `StandAt`, `Feet`). Errands (`Errand`): the engineer walks to the part it sees to and back (`EngTag`: the part by tag, not an index);
  a loader walks to the fullest source, takes up to 200 rounds, walks to the box and puts them in (`LoadStage`, `LoadCarry`); repellers
  (Repel) walk to the first boarder until within 6 m, then their bot brains fight (`CrewRepelWalk`). A record's walk is a timer as before
  (straight distance x 1.4 with a graph).
- `--crew-check [SHIP] --crew-walk [--speed V] [--turn DEG/S]`: the graph built (timed cold and warm), a standing body walked by `Steer`
  through the real movement step from a couch to every station, the ship 30 m up and moving.

Verified (2026-11-17): `--crew-walk` on the Zero Day, still and at 10 m/s turning 10 deg/s: 9 of 9 stations reached (helm 3 m in 0.9 s, gun
seat 238 35 m in 9.8 s, engine room 37 m in 11.8 s; through the bulkhead doors, ends 0.3 m off). Graph 72150 spots (66 parts), warm build
113 ticks, 45 ms in all, worst tick 0.49 ms; walking, the nav's worst tick 1.0 ms (searches), 0.04 ms on average. Window runs on the crewed
Zero Day: a gunner shot, a spare walks 8 m to the gun in 3.2 s; the pilot shot, the same walks to the helm; a drive core broken down, the
engineer's body walks 8 m to it (1.9 s), repairs it, walks back; the engineer killed, a spare walks 37 m to the engine room in 13.5 s and
takes the job. `--crew-check` 34 us a tick, 720 bytes in 7200 ticks (as main). `--crew-scale 5000` on Zero Days 58 us a tick, worst 1.1 ms,
0 bytes (records have no graphs).

Costs to know: the first graph of a session costs about 15 ms in one tick (JIT) and its arrays' first growth (about 3 MB once).

Unverified: a joined client watching a crew body walk (it saw the 6 bodies; that run's frigate fell out of the world at 7 fps with two
windows on the 2-core box, not seen in five host-only runs: unexplained); lifts (nothing rides one: a deck on a lift is a spot where it
stands now); a repeller's walk to a boarder (no run); a loader's carry in a run (the Zero Day has no ammo box); Lock down and Dump.

## Done (phase 7: work crews)

- Stations `Spot` (beside the capital gun: the biggest gun part, 1.5 m3 or more: the Zero Day's Railgun), `Crane` (each lifter: arm or
  bridge crane, worked from beside it; the Lifter Cab stays a person's), `Dock` (each cargo dock), `Build` (each Building Station), at most 12
  work stations a roster; jobs `Spotter`, `Crane`, `Hauler`, `Builder`, filled after the magazine, before the couches; standing (bodies walk
  there by CrewNav).
- Rule 1 for machines (`Source/Constructs/CrewGate.cs`, `CrewWorld.Works`): on a crewed NPC body (a faction kind, not an own crew or a prize,
  owner nobody) a lifter (`ConstructLifters`: stops as without power), a cargo dock's exchange (`LinkedLogisticsDocks`) and a Building
  Station's job (`ConstructStation.StepOpen`: waits, still open) work only while their hand stands at them. Players' bodies and route
  ships are never held.
- A spotter at its place: that gun fires within half the bearing (`ShipAi.SpotBear`).
- A nobody's body set down by the vehicle spawner with a lifter, dock or Building Station gets a crew (`CrewFactionVehicle`).
- `--crew-check BLUEPRINT --crew-work [--crew-faction NAME]`: stations, hands, the gate with the hand killed and after a spare takes it.

Verified (2026-11-17): Factory Showcase as Traders: a builder at the Building Station, three crane hands at the Loader Arms; each killed,
the gate shuts (`works False`) until a spare walks over (two spares: the last two stay shut). Linked Yard: a hauler at the Docking Clamp, a
builder at its station. Zero Day: a spotter at the Railgun (the gun spotted while manned, not after).

Unverified: a lifter, dock or station actually held and released in a running world (only the gate's answer is checked); builder drones
are crew members, not drones (the design's "own design first"); no protocol change (none needed).

## Review fixes (branch `claude/bot-brain-3`, 2026-11-17)

- Placing crew never wakes a body (`CrewSit`, `CrewStandAt`: a parked ship stays parked; its drive wakes it on its keys); a crew body's
  weight and landings never push or wake the body under it (`ConstructWorld.IsCrewBody`, in `NoteStanding` and `BlockPlayer`); a parked
  body (asleep, anchored or still) gets crew bodies only with the server's ground or a static under it within 60 m (`CrewGroundUnder`).
- The machines' gate is the world's own hook (`ConstructWorld.CrewWorks`, set with `CrewBytes`), not a process-wide static.
- A client's roster reads every job (Spotter, Crane, Hauler, Builder were shown as Loader).
- Walkers open only a door on the leg they walk (within 0.9 m of it, ahead, within 1.8 m) and shut it again 2.5 m behind, unless their
  ship is boarded or they are fighting.

Verified: a joined client by a parked Traders frigate with the host 700 m off: it landed and stayed (8 m, 0 m/s for 40 s), the client saw
its 7 crew bodies, no fall (the earlier fall wasn't seen again). `--crew-walk` at 10 m/s turning: 9 of 9 (doors shut behind); `--crew-work`
Factory Showcase and Linked Yard OK; `--crew-check` OK (39 us a tick); `--linked-check` passed; sections Ride frame, Seated riders,
Sandbox spawns, Frame allocations, Large systems network, Salvage haul network, Mode switch reset pass.

## Done: boarded crews fight back (Karl, 2026-11-18)

Karl: "when boarded, NPCs should take up arms and try to defend themselves". Built on phase 5's acts, not instead of them
(`Game/GameCrewDefend.cs`, called from `ICrewHost.Board` each captain's look):

- Crew bodies carry a sidearm at their stations (a launcher or a non-pistol secondary becomes the P9; a sniper or launcher primary the BR-1;
  no grenades aboard). The long gun is in the lockers: an arms point is a Gun Rack or a part named Locker, not a suit's or a medicine one.
- Who stands up, under every act but Surrender and Abandon (and with no faction, or while a scuttle runs): the off-duty crew (couches, no
  station); under Repel also the work hands (loader, spotter, crane, dock, builder), the engineer only if its faction never scuttles, and the
  gunners once the boarders are half the crew left. The pilot keeps the helm. This replaces Repel's old stand-up and walk-to-the-boarder.
- Each fighter: walks (CrewNav) to the nearest arms point on its part of the graph, takes its long gun in 1.5 s; then every other one
  holds the bridge (stops 3 m from the helm), the rest the breach (where the first boarder was seen aboard: stops 7 m short). Holding, it
  crouches and comes up for a look every 3 s, stands to shoot, ducks to reload or when hit. A breach guard with nobody seen for 12 s hunts
  (walks to the nearest boarder, through doors) and opens a shut door between it and a boarder from 2.5 m. A walker that sees a boarder
  within 30 m stops and fights (one on its way to a locker only within 8 m, and goes back for the gun once they are gone, twice at most).
- Sight and shots: the bots' `BotSees` reads only the world, not constructs, so it saw through every wall aboard (300+ rounds into the
  hull a run). A fighter sees a boarder only with no ship part on the line to where it aims (`SandboxRay`), and holds fire while one is on
  its round's line. Its aim (`GuardAim`) is worked along the target's own up: `BotAim` aims along the planet's up, which on a rolled ship
  (the Raiders' Zero Day lay on its back in the check) or in space put every round into the deck.
- Twitch's rules stand: morale, the acts, surrender, scuttle (still only the engineer), capture. A hit on the ship's hull by its own crew
  body is no longer a foe or a deed (`CrewWorld.OwnBody`): their stray rounds had marked their own crewmate a foe.
- `CrewWorld.Errand` takes how far from the feet a walk may start (`fromAcross`, 1.2 m as before; fighters 2.5 m: a work hand stands
  beside its machine, off the walked spots).
- `--crew-check [SHIP] --crew-board [--crew-faction NAME] [--seconds S] [--mortal]` (`Crew/CrewBoardCheck.cs`): a host and a joined
  client; the ship settles, is crewed, the host stands aboard as a foe; checks crew up, long guns, posts, shots, the client seeing a gun
  change, then shoots the crew one at a time until it gives up by its rules.

Verified (2026-11-18, the Zero Day): Raiders, Mercenaries, Militia: 3-4 crew up, 1-2 took long guns, posts held, a breach guard opened a
bulkhead door on the boarder; with `--mortal` each crew killed the boarder (4-6 rounds). Losses: Raiders scuttled, Militia and Mercenaries
surrendered, nobody fought on after. Traders surrender at once (their rules), so nobody stands up. `--crew-check` fight: 78 us a tick.

Unverified: a boarding in flight or in orbit (the check's ship is landed); the Limpet's cut hole as the breach (the breach is where the
first boarder stood); two boarders; a client's view of a crew body crouching.

Seen, not fixed: a ship built over the ground drops about 2 m at up to 7 m/s as it settles, and riders on its decks (crew bodies and the
host's player) sank 0.25 m into the deck or fell off the hull; work hands standing on the docked Shuttle stand off the Zero Day's graph
and fight with their sidearms where they are.

**Follow-up (Karl, 2026-11-18, in-game):** seated gunners stayed in their couches while he walked past them. `CrewDefends` kept a station's crew seated under every act but Repel, and kept the gunners while the ship had a target lately, which included the boarder himself on his way in. Now every act but Surrender and Abandon stands up everyone but the pilot at the helm, a miner on the ground and the engineers of a faction that may scuttle; gunners stay only while their guns fought another craft in the last 10 s (`GunsFightCraft`) and the boarders don't outnumber half the crew.

## Next (for a successor)

- CrewNav: lifts (ride a lift deck: `ConstructRide` carriers), patching a graph on a part's loss instead of building it again, Lock down
  (locked doors cut the graph) and Dump (cargo out) in the boarding acts; a joined client watching a walk.
- Own crews: pilots for `GameNav` and linked-logistics route orders (own crews never take the helm now), the Crew Console part and
  orders at range, Robot Shells on the roster (the next kind, answer 4), a brig part (prisoners are held aboard without one), ransom.
- Earning credits beyond the start balance and the capture bounty (a placeholder: trade comes later).

Scale after phase 6 (2026-11-16): `--crew-scale 5000` on Zero Day frigates 11 us a tick (worst 93 us), on Bandit Buggy 1 as Raiders 55 us
(worst 0.69 ms), 0 bytes; the fight check 26-29 us a tick, worst 0.7 to 4.1 ms once (driving: Karl's rays or JIT), and a few hundred bytes
in 6 of 7200 ticks (none in the scale runs: taken for tier-up JIT, unproven). Self-test sections by name after merging main (protocol
332): Sandbox spawns, Sandbox body sync, Frame allocations, World links, Space ships, Save folder: all pass.

## Hiring before currency (proposal for the user, answer 4)

Recommend: **recruit, don't buy.** A crew member can be hired when they are (a) surrendered on a ship you took, (b) rescued
(an escape pod or a derelict's survivor), or (c) at a faction outpost whose standing with you is friendly, for a placeholder
fee in materials (a crew member's "keep": rations or refined goods from `Constructs/Resources.json`, data in the faction file's
`hire` key) until currency exists. Each hired member needs a couch or bunk aboard (a crew cap per ship). Or: hire only from
prisoners (simplest, no data), or a flat materials fee anywhere.

## Unverified

- Aircraft pilots as crew (`PilotHeld`): built, never flown with a pilot killed.
- Stand-ins' look (no picture taken); whether clients' part indices always match the host's after a rebuild (the host
  re-sends on its rebuild; a client's own rebuild order is assumed the same).
