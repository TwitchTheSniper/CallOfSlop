---
paths:
  - "Source/App/SelfTest*.cs"
  - "Source/Game/TestTools.cs"
  - ".github/workflows/**"
  - "docs/sandbox/constructs/self-tests-and-history.md"
---

# Self-tests and CI: what went wrong here before

**Tests only when asked, and the suite doesn't grow** (CLAUDE.md, "Rules the user set"): add no section to the full suite without being asked; a section made for a failure goes into `SelfTest.Archived` and is run by name, or replaces one. Never raise `SelfTest.MaxSections` or `MaxSectionSeconds`. Tests that are asked for are short, seeded and scripted; remove temporary or diagnostic test code before finishing.

Each line below is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- Seeded digests (`SelfTestModeModules.cs`) and pinned hashes (`SelfTestPackAssets.cs`) change with gameplay timing, props and maps; re-record them from the failure message. Many tests use the first map (Proto Box, `order` 1): new maps get a high order. CPU-time tick checks measure on Windows only (`SelfTestTiming.Available`).
- An offline scene with bots spawns the local player through `SpawnLocalSafely`. A wall-clock loopback check waits for the state it needs with a deadline (`WaitFor`, `MatchWithin`), and frame loops never burst after a stall.
- An unknown part id in a test is `ConstructParts.All.Count`, never a fixed number. A scripted-clock fire test resets `NextFireTime` after giving a gun.
- A section that changes a shared map restores it in its `finally`. A section that fails in its part of the suite but passes alone depends on a neighbour's leftovers: bisect with `--selftest-only "Earlier, Failing"` pairs, give it a `needs`, or reproduce with a manual full run plus a DIAG line, not by guessing at neighbours.
- A wall-clock check waits for the state it needs with a deadline, judges medians, leaves out what the peer has no poses for, subtracts what a frame's time explains, and scales limits to speed; a wait's condition is false until the thing has happened; a client's player is moved through the server (`PwMoveClient`).
- Game code stamps tracers on `ConstructRender.Now`; a check picks its own events by what they are and measures a flight by its own ticks (`Age`). Pool slots past the count are stale.
- A check measures what the game does (e.g. `ConstructWeapons.AimWay`), not a proxy; a classifier is tried on a case for each class; a test's message says what it measured.
- A catch for an expected miss names what it expects; look a part up outside the try.
- When "SLOWEST" or DONE nears the workflow's limit (now 1800 s), raise it or trim sections.
- A check that loops over every blueprint costs by its size: sample the giant ones (`n * n` capped).
- A check that reads a limit or a count takes it from the constant or the generator (`SurvivalMaxMass * 0.3f`, `EngineKitParts.Grades`, `SandboxTiles.ResourceSlot`), not a number picked against the old value; a "bad index" is past the wire range, not a fixed 255.
- A change that moves an effect to a new path updates the checks that count the old one (`ShellsLanded`).
- A section that opens a window through real input closes it in its `finally` (`Program.SweepEnd`): a window left open ate every later section's clicks, twice (2026-11-10, 2026-11-13).
- 2026-11-13: "Powder charge" failed about one run in a few hundred: seven low rounds in a row from a 6 degree hip spread met the ground before 100 m. A check on a spread weapon's single-round outcome samples until the outcome it needs has happened (or a cap), and never trusts a fixed handful of random rounds.
- 2026-11-13: "Press stock" looked the server's part index up in the client's copy after a fixed 0.3 s pump, and the copy sometimes still held a removed part. A network check waits (with a deadline) for the client's copy to match before using a server part index on it, never a fixed pump.
- 2026-11-13: "Physics gun smooth" failed on one watcher sample under 30 ms jitter and 2% loss: a pose lost and the next one late outruns the copy's cover. A smoothness check judges its worst sample only where the client still had a pose to draw toward, and counts the frames past the newest pose under a limit of its own.
- 2026-11-13: "Sandbox physics gun feel" failed about one run in five: the watcher got no poses for 0.8-1.5 s. Every gap was a blackout on every link, both ways, at once (the host's send queue empty, one reliable message resent five times unacked), and none came with RakNet's simulated loss at 0%: RakNet's `ApplyNetworkSimulator` on three peers in one process loses together (its shared random numbers), a network no player is on. Rule: a loopback check that needs independent loss between several peers runs them through `SelfTestLossyRelay` (its own Random); a gap on every link at once is the harness, not the game.
- 2026-11-13: "space queries allocated 1624 B" once on CI: its warm-up called one frame's pose while the measure called every frame, the bubble and the ecliptic conversion, so first calls landed in the measure. A warm-up runs the measured loop's own body over every input it will see.
- 2026-11-13: two verification runs found "Could not reach host" on a joined client: the window tool's `wait N` counts frames and its runs are uncapped (160 fps or more), so the host's script ended before the client joined; `put`'s "a minute" retry is also frames. In a host and client pair, time the host's waits with `secs N` (real seconds) and start the client's steps after the host's `put`/`bring`.
