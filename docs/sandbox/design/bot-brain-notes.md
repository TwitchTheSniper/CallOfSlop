# Bot brain: builder's notes

Working notes for docs/sandbox/design/bot-brain.md, kept current after every step so a successor can finish. Branch `claude/bot-brain`.

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
- Protocol 328: `Msg.CrewSeats` (u16 body, u8 n, n x u16 seat part).
- Data: `Crew/Names.json` (`CrewNames`).
- `--crew-check` replaces `--ship-ai-check` (Source/Crew/CrewCheck.cs): fights, then kills the pilot (40 s), the gunners (70 s)
  and the rest (90 s); `--scale N` times the crew alone at N members.

## Next

- Phase 1 by hand: a window run with a crewed ship (bodies within 300 m, shoot a gunner from outside), a joined client's view
  of bodies and stand-ins.
- Phase 2 (ground crews), 3 (factions, `Crew/Factions`, captain), 4 (CrewNav, engineers), 5 (boarding, scuttle 15 s / 90 s / 5 s,
  capture when all crew are gone, prisoners), 6 (own crew: hired NPC crew first; see "Hiring" below), 7 (work crews).

## Hiring before currency (proposal for the user, answer 4)

Recommend: **recruit, don't buy.** A crew member can be hired when they are (a) surrendered on a ship you took, (b) rescued
(an escape pod or a derelict's survivor), or (c) at a faction outpost whose standing with you is friendly, for a placeholder
fee in materials (a crew member's "keep": rations or refined goods from `Constructs/Resources.json`, data in the faction file's
`hire` key) until currency exists. Each hired member needs a couch or bunk aboard (a crew cap per ship). Or: hire only from
prisoners (simplest, no data), or a flat materials fee anywhere.

## Unverified

- Everything with bodies (`Promote`, `CrewSit`, a body killed in its seat) is untried in a real window and over the network.
- Stand-ins' look (no picture taken); whether clients' part indices always match the host's after a rebuild (the host
  re-sends on its rebuild; a client's own rebuild order is assumed the same).
