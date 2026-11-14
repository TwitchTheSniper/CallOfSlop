# CI flakes 2 (2026-11-14)

Two checks failed once each on main's CI on merges that did not touch their code.

## Nudge tool, online: "the arc's turn went wrong (taken True, 120.00 deg, about Z True)"

Linux, shard 1 of 3 (run 43610258169, job linux (1)). Its line: "turned 120.00 deg about up True, the centre stayed
58.2 mm". The landing was seen; the failure was the centre (limit 5 mm).

Cause, a game fault a joined client's player sees: the client makes the arc's pivot from its own copy's mass centre
(`Program.SandboxNudge.cs`, "The pivot") and sends it in `NudgeBegin`; the server turned its own bodies about that
point (`ConstructEdits.NudgeBegin`, `hold.Pivot`). Just after a drag the copy can be centimetres behind (the client's
prediction stops within 5 mm of the target, then the copy shows interpolated poses still on their way back), and a
turn of angle a about a point d off the centre moves the centre 2 sin(a/2) d: 1.7 d at 120 degrees. CI's 58 mm is a
copy about 34 mm behind; here the copy was 0.1-1.3 mm behind (25 runs) and the centre moved up to 2.2 mm.

Fix, in the game (`GameSandboxNudge.cs`, the server's `NudgeBegin` handler; `ConstructEdits.NudgeCentre`): a nudge
begun over the network turns about the bodies' mass-weighted centre as the server has them. The sent pivot is only ever
that centre (the client's one sender), so the wire format and its meaning are unchanged (no ProtocolVersion bump).
`NudgeBegin` itself still takes any pivot (the offline part turns about one off to one side).

The check is back to the hard case: the arc taken straight after the put-back drag, no wait, the 5 mm limit as before;
its line now gives the client's pivot's offset, and its failure message the landing and the centre's move.

Runs, this PC, "Nudge tool" alone: before 25 runs, no arc failure, the centre moved 1.7 times the client's offset (up to
2.2 mm); after 20 runs, no failure, the centre moved 0.0 mm in every run with the client's pivot 0.02-0.25 mm off.
Not tried in a window by hand: the window tool has no Nudge verbs; the section's online part drives the real tool
(aim, clicks, mouse drag) on a joined client under 40 ms lag and 5 % loss.

Left: the client's own prediction still starts from its copy's stale pose and pivot, so during such a drag its drawn
body can sit a few cm from the server's until the server's poses take over (no effect on where it lands).

Seen once in the before runs, unrelated: "the drag isn't smooth: holder 3.8 mm, 42 back" (1 of 25).

## Space ships: "the predictor allocated 7152 B"

Windows, shard 3 of 3, where Space ships runs second, after Terrain streaming.

`SpaceShips.Predict` and everything it calls allocate nothing in code (no lists, closures, lazies; `Forces` is empty
here). Measured on this PC: 0 B in 3000 repeated calls after the warm-up; 40 runs alone, 60 runs of "Terrain
streaming, Space ships" as ten copies at once with two processors each, runs with `DOTNET_TC_CallCountingDelayMs`
(0-60000), `DOTNET_TC_CallCountThreshold` (1-50000), `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`,
`DOTNET_ReadyToRun=0`, OSR counters at 1, and another thread forcing 75,000 GCs during the calls: never a byte.
Most likely cause: a one-off on the measuring thread (JIT tier-up or a lazily made runtime structure) landing in the
check's single measured call on a slow runner.

Fix (test only, `SelfTestSpaceShips.cs`, Karl's section): the measure is `SteadyAllocBytes` (two warm-ups, the least of
three identical calls), the repo's way for allocation probes since 2026-10-28. Still strict: a real allocation shows
in every round. Runs after: 40 alone, no failure.
