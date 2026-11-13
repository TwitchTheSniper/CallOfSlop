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
