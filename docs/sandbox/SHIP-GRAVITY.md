# Ship gravity (felt acceleration)

A world option (Sean, 2026-11-23), chosen when the world is made and kept with it, like its death rules. The study behind it,
with the measurements, is the "Ship gravity from acceleration" doc (https://claude.ai/artifact/Tjhjxc4sxdvbtSQmrtqY3K).
Code: `Source/Constructs/ShipGravity.cs` (the rule), `Source/Game/GameShipGravity.cs` (the world option, harm).

| Rule | What a rider feels |
| --- | --- |
| **Deck gravity** (default) | 1 g towards the deck's floor, wherever the ship is, and the hull's acceleration past 0.25 g on top (Karl, 2026-11-23): heavier along your down (a hard burn pins you to the deck; nothing slides you) |
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
- **Deck gravity's inertia** (Karl, 2026-11-23: in a high-g burn riders still jumped about). `Construct.Felt` is worked out
  under every rule; with deck gravity it is only the hull's acceleration (`-Accel`, or in space `-SpaceAccel`), and
  `SetRider` puts the deck's 1 g along the rider's down plus that acceleration less `DeckFeltFrom` (0.25 g, faded in, so gentle
  driving and a cruise feel as before) into `RideFelt`. The same moving rules below then slow and pin them, but `Rider` takes the whole of it along their down
  and pushes nothing sideways (a turning or braking vehicle keeps its riders on their spots, as the ride-frame tests ask). No harm with deck
  gravity (Karl not asked).
- **Felt-g readout** (Karl, 2026-11-24; moved into the vitals panel, Sean 2026-11-24: `UI/UiVitalsPanel.cs`, `VpGravity`): a
  block under the panel's rows, always shown: GRAVITY and the way in words ("DOWN, BACK", or WEIGHTLESS), a dial in the bars'
  column (the pull seen from behind them; its end filled while it presses them back, a ring forward, else a head; its length
  the g over 3 g to the power 0.35) and the g on the right ("1.0 G"). The server works out each person's pull (`ShipGravity.PersonPull`: seated,
  standing aboard, on the ground, or nothing in free fall) and the owner's snapshot carries it (protocol 449). Cyan, amber
  from 1.5 g, red and PINNED from 3 g; past the g they take unhurt (`ShipGravity.Limit`, the harm rule below; never under
  deck gravity) it pulses CRUSHING and the plate turns red. Not shown in a robot shell (the shell's panel is there instead).
  Under deck gravity the deck counts as 1 g (`G`), and the mover's own (stronger) deck gravity is scaled by the g felt.
- **Magnet boots** (Karl, 2026-11-24; `Game/GameSandboxMagBoots.cs`): key 7 (`GameAction.MagBoots`, binds 35), sent with
  each command (`InputCmd.MagBoots`). On and standing on a deck with a felt pull: held to it (at least the mover's 1 g, no
  push, no jump, walking at `BootsSpeed`). Floating with them on, a deck within `BootsReach` (2.5 m) under the feet draws
  them down at `BootsPull` (9 m/s2) until they touch it (`ConstructRideMove`). The vitals panel's MAG BOOTS row (bottom
  left) shows ON or OFF and the key.
- **Moving.** Past `SlowG` (1.5 g) moving slows, down to a fifth at `PinG` (3 g), where a standing rider can't move or jump.
  Under `FloatG` (0.15 g) a rider in the air drifts: the move keys, Jump up and Crouch down, at 2 m/s. On a floor they still
  walk (no gravity pulls them off it); a jump pushes off.
- **Harm.** Once a second, a person past `HarmG` (6 g) takes `HarmPerG` (6) health for each g past it. A seat takes 2.5 times
  as much (15 g), a space suit 1.3 times (both: 19.5 g). A seat with its own rating (`"seat": {"g": N}`, `ConstructPartDef.SeatG`)
  holds N g: the Crash Couch holds 40 (Sean, 2026-11-23). Death says "crushed by N g". Robot shells and bots are not hurt.
- **Artificial gravity.** Inside a Gravity Generator's box the generator's down holds as before, under every rule.
- **Loose bodies aboard** take the same rule: no deck pull (`ConstructFrames.Pull`); in space the flight's drive and hits reach
  them as before (`FreeFall`), in the bubble the world's pull and the hull's contacts give true physics.
- **Cargo in 0 g** floats as it would near people. With nobody within `WatchRange` (30 m), a body in a 0 g frame is slowed
  (`ShipGravity.Settles`, about the hold's air) until it sleeps, so a hold nobody is in costs nothing. When a frame's pull
  changes, at most `WakesPerTick` (24) sleeping bodies wake a tick; the rest wake in the ticks after.
- **Not done yet:** crew bots stand on the walk graph by the deck's up and feel nothing; a coasting sleep (drifting cargo moved
  by formula with no solver step and no pose stream, in the study) is not built.
