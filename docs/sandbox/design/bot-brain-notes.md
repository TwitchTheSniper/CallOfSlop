# Bot brain: builder's notes

Working notes for docs/sandbox/design/bot-brain.md, kept current after every step so a successor can finish. Branch `claude/bot-brain` (phase 1, merged), then `claude/bot-brain-2` (phases 2 on).

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
- Protocol 331: `Msg.CrewSeats` gains u8 faction kind and u8 crew alive; the look line adds "(Militia, crew of 3)"; crew bodies are
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
- Notices go by `SandboxHands.Notices` to everyone aboard or within 200 m (no new message). `Msg.CrewSeats` (still 331 on this branch)
  also carries the scuttle point and its stage, for the client's look line.
- Window verbs: `crewfaction NAME` (a test state), `crewdisarm` (a server shortcut: stands the player by the charge and starts the
  disarm through the Mend act's server path), `crewprisoner release|drop|hold|hire`.

Verified (2026-11-16, window runs on the crewed Zero Day, the player sat in a Crash Couch): as Traders, surrender at once (morale 47%),
taken by player 0 with 5 prisoners, one released. As Raiders with gunners and pilot shot: Scuttle; the gunner-turned-spare had become
the engineer... (a spare who walks to the engine room takes the job); armed at the Ammo Crate, went off 90 s later (the crate cooked off);
another run, `crewdisarm`: "player 0 disarmed the scuttle charge", the crew went for another crate.

Unverified: Repel with bodies fighting a boarder (no run with spares on couches and a boarder on foot); a joined client's look line on
a charge; Call for help; Abandon; boarding a ground vehicle.

## Next

- Phase 1 is done (below). Phase 2 next: the ground `HelmDriver` from the brief, convoys and faction vehicles spawned crewed.
- Phase 2 (ground crews), 3 (factions, `Crew/Factions`, captain), 4 (CrewNav, engineers), 5 (boarding, scuttle 15 s / 90 s / 5 s,
  capture when all crew are gone, prisoners), 6 (own crew: hired NPC crew first; see "Hiring" below), 7 (work crews).

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
