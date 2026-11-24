---
paths:
  - "Source/Net/**"
  - "Source/Constructs/ConstructNet*.cs"
  - "Source/Game/Game.cs"
  - "Source/Game/GameSnapshots.cs"
  - "Source/Game/GamePacketSafety.cs"
  - "Source/Game/GameNetStats.cs"
  - "Source/Game/GameHostBye.cs"
  - "Source/Game/GameSandbox.cs"
  - "Source/Game/GameSandboxStream.cs"
  - "Source/App/NetClientProcess.cs"
  - "docs/net/**"
  - "docs/sandbox/constructs/network-and-scale.md"
---

# Network: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- **Protocol.** Any wire-format change bumps `ServerBrowser.ProtocolVersion` with a comment line in the history above it; new messages go at the end of `Msg`; when two branches bump it, the merger renumbers. New part files also move part ids, which both peers must share. The commit guard (`.claude/hooks/guard.sh`) refuses a commit that changes `Msg`, `CActKind`, `CBodyKind` or a packet read or write on a branch that has not bumped it.
- Prove networking fixes with separate peers over the real transport, with lag and loss.
- Anything written into a fixed budget in a fixed order takes turns (a cursor) and keeps room for its refresh; drive it past one packet (`ConstructNet.WritePoses`).
- A network-fed list with a cap starts at its full size (`new(Max + 1)`), or it allocates in a steady frame after a hitch.
- Bodies are drawn on the server's tick stamps (`ConstructNet.SampleAt`, `ConstructWorld.NetRenderTick`); the holder draws the aim plus a smoothed correction. Judge held and driven bodies by smoothness (second difference, back-steps), not final position. A buffer of past states covers the largest delay plus a sample either side, and a jump when new data arrives is eased (`ConstructNet.Smooth`).
- At a merge, read every flags byte and every `(byte)` cast by hand: a bit clash or a narrowing cast shows no text conflict.
- Per-match Player fields are marked `[MatchState]` and reset in `Player.ResetForMatch`; per-player state outside the Player is cleared in the mode module's `OnPlayerReset`, both via `Game.ResetPlayersForMatch`.
- A per-body history is checked against the body's structure before it is touched; network tests edit a body while it moves; a growing `[net] dropped` count is the first thing to read in a user's log.
- Everything drawn together is sampled at one tick; when something looks wrong on a client, check the host alone first.
- 2026-11-09: a Sandbox client still drew a far horde the host didn't have: the zombies' sends stopped with the last zombie (no empty cells went out) and a full Msg.ZCrowd always restarted at cell 0, starving the last rows of a big crowd. A delta stream sends its final empty state and resumes where a full message stopped; measure what the client holds against the server's counts (`--zrange-host`, `CrowdStale`).
- 2026-11-10: with 281 shipped blueprints the parts menu spawned the wrong blueprint or none: `CActKind.Blueprint` sent its index in one byte. An index into a list that grows with data goes on the wire in two bytes, and a test's "bad index" is past the wire range, not a fixed 255.
- 2026-11-11: conveyor riders froze on a client 1 s after the server's last word (drawn from a clamped clock, never stepped there). Anything a client draws moving between messages is stepped on the client by the server's rule.
- 2026-11-12: a pick from a machine's own Recipe button stayed on the host: `OwnRecipe` returned before `StructureDirty` was marked. Any server path that changes a part's setting marks its body dirty; try a setting change once on a joined client.
- 2026-11-12: a joined client saw no Crafting Terminal orders once a press order chained a blank: a chain line's kind went as a u16 id, and the blank is a run-time piece whose id is the host's own, so the client threw the whole packet away. Any part id in a packet that can be a run-time part (a chain line, a claim, a rider) goes by `ConstructStorage.WriteDef`/`ReadDef` (its key), never `U16(id)`.
- 2026-11-13: remote players carried on past their newest snapshot at its velocity stepped back when the next one came (a rider stepping off a moving seat slows in the air). Anything drawn ahead of its newest network sample uses the motion the samples show, not a constant velocity, when the thing is known to slow (`Game.SampleSnapshots`).
- 2026-11-13: Construct poses went `UnreliableSequenced` on RakNet channel 0, where every `ReliableOrdered` message also goes, so a lost reliable packet held the poses behind it until its resend (under 2% loss the watcher's lead fell below 2 ticks in 13 of 60 sweeps, drawn past its newest pose in 4; plain `Unreliable`: 2 and 0). Rule: a stream the reader orders itself by tick (`ReadPoses` drops older ticks) goes `Unreliable`, never sequenced on a channel shared with reliable-ordered traffic.
- 2026-11-13: A Building Station draining a parked Fuel Tank left joined clients reading it full: the fuel pool and battery charge ride only the pose, and a still body sent one only when its signals changed (plus a slow round-robin refresh). State that rides the pose must also wake a still body's pose when it changes (ConstructNet.StateDue, rate-limited); state a still body changes without moving needs its own on-change path; and an older Full (a big body's kept base on a resync) must not overwrite what a newer pose set.
- 2026-11-13: a Steam ID claimed over RakNet (any value; every player's ID is told to all) found a saved world's record, though `Player.SteamId` said "never trusted". Anything a client reports about who it is finds saved state only once verified (`Game.TrustedSteamId`); a field marked untrusted is checked against every reader before a new use.
- 2026-11-16: every anchored body without joints on a joined client was sent whole again every digest round (2 s), found when "Frame allocations" moved to Construct Yard: the digest calls a body asleep by the server's `Still` (anchored and jointless counts, awake or not) while the client compared its own `Asleep` flag, which such a body's last pose had left false. A digest field is compared on the client by the same rule the server wrote it with, not by a neighbouring flag.
- 2026-11-16: a joined client's ship card and orbit line said "under the ground" for a ship the host had 2 km up at 100,000x: the client worked the orbit out from its own copy of the construct's pose, which lags the host's, and a client never sets `Construct.SpaceShip`. Rule: a client reads a ship in space from the host's ship states (`Game.SpaceShipIdOf`, `NavState`, `SpaceSession.SampleShip`), not from the construct.
- 2026-11-18: a joined holder's physics-gun piece hopped a tick's travel now and then (845 m/s2 in "Physics gun smooth" on Windows CI): its simulation stepped up to `_moverTick`, and the stamps' steering (`SteerMoverClock`) moves that clock a tick without a local tick. A client prediction steps once per local tick (`_moverCmds`) and only takes its labels from the steered clock.
- 2026-11-18: `enum Msg : byte` is full (255 values): a new message appended at the end doesn't compile (CS0543). Take a `Removed...` slot in place (rename it, keep its number, say so in the comment and the protocol line) until the ids are widened.
- 2026-11-19: a Material Bundle's CBody was refused on every joined client ("setting 20 of a Material Bundle", thousands a second), so that body never updated there: its Setting is a material id, which the save reader allowed (`Yield != 0`) and `ConstructNet` checked against its settings count. A part whose Setting means something else is exempted in every reader at once: grep `Settings.Length` across the readers.
- 2026-11-24: the Scrap Breaker took the store's last free flag bit (the second flags byte's 128) and every joined client dropped the whole CBody ("a store with more flags 128"): `ConstructStorage.Read` refused any bit past the ones it knew. A new flag bit is checked against the reader's guard for unknown bits (grep the byte's `throw`), and a wire change is tried on a joined client (`--net join:127.0.0.1`, then grep `callofslop-join.log` for `dropped a bad`).
