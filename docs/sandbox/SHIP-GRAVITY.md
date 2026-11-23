# Ship gravity (felt acceleration)

A world option (Sean, 2026-11-23), chosen when the world is made and kept with it, like its death rules. The study behind it,
with the measurements, is the "Ship gravity from acceleration" doc (https://claude.ai/artifact/Tjhjxc4sxdvbtSQmrtqY3K).
Code: `Source/Constructs/ShipGravity.cs` (the rule), `Source/Game/GameShipGravity.cs` (the world option, harm).

| Rule | What a rider feels |
| --- | --- |
| **Deck gravity** (default, as before) | 1 g towards the deck's floor, wherever the ship is and whatever it does |
| **Felt in space** | ships in space (out of the bubble, or a bubble with no pull) have no deck gravity: at rest you float, a burn gives weight along it; on a planet, decks as before |
| **Felt everywhere** | every vehicle, everywhere: the world's pull less the vehicle's acceleration (true physics): turns push you sideways, a parked ship pulls you the planet's way |

- **Picked** on the Single player page (a new world) and in the lobby room's Match tab (Creative and Survival). Saved in the
  world's meta (`WorldMeta.ShipGravity`, after `Death`; older files read 0, deck gravity). Settings key `sandbox_ship_gravity`.
- **Felt pull.** Server, once a tick per body in `ConstructRide.BeforePhysics`: `Construct.Felt` is the world's pull less its
  acceleration (`Accel`), or in space minus the drive's push (`SpaceAccel`; nothing at a raised time speed), filtered over
  `FilterSeconds` (0.12 s) so a contact's one-tick spike isn't felt. A docked craft's riders feel the carrier's.
- **Riders.** After a rider's step (`ConstructWorld.BlockPlayer`, server) `ShipGravity.SetRider` puts the pull at their chest
  (with the hull's turn throwing them outwards) into `MoveState.RideFelt`, in their segment's frame. The owner's snapshot
  carries it (protocol 444), so a client's replay steps with the server's value. In the mover the part along their down is
  the gravity; the rest is a push (sideways in a turn, up to the ceiling under hard braking).
- **Moving.** Past `SlowG` (1.5 g) moving slows, down to a fifth at `PinG` (3 g), where a standing rider can't move or jump.
  Under `FloatG` (0.15 g) a rider in the air drifts: the move keys, Jump up and Crouch down, at 2 m/s. On a floor they still
  walk (no gravity pulls them off it); a jump pushes off.
- **Harm.** Once a second, a person past `HarmG` (6 g) takes `HarmPerG` (6) health for each g past it. A seat takes 2.5 times
  as much (15 g), a space suit 1.3 times (both: 19.5 g). Death says "crushed by N g". Robot shells and bots are not hurt.
- **Artificial gravity.** Inside a Gravity Generator's box the generator's down holds as before, under every rule.
- **Loose bodies aboard** take the same rule: no deck pull (`ConstructFrames.Pull`); in space the flight's drive and hits reach
  them as before (`FreeFall`), in the bubble the world's pull and the hull's contacts give true physics.
- **Cargo in 0 g** floats as it would near people. With nobody within `WatchRange` (30 m), a body in a 0 g frame is slowed
  (`ShipGravity.Settles`, about the hold's air) until it sleeps, so a hold nobody is in costs nothing. When a frame's pull
  changes, at most `WakesPerTick` (24) sleeping bodies wake a tick; the rest wake in the ticks after.
- **Not done yet:** crew bots stand on the walk graph by the deck's up and feel nothing; a coasting sleep (drifting cargo moved
  by formula with no solver step and no pose stream, in the study) is not built.
