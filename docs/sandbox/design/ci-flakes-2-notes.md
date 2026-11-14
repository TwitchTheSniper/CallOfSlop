# CI flakes 2 (2026-11-14)

Two checks failed once each on main's CI on merges that did not touch their code.

## Nudge tool, online: "the arc's turn went wrong (taken True, 120.00 deg, about Z True)"

Linux, shard 1 of 3. The angle was a whole step and about the right axis, so the failing condition was the centre
(it must stay within 5 mm) or the server's landing.

Cause: the client makes the turn's pivot from its own copy's centre of mass (`Program.SandboxNudge.cs`, "The pivot")
and sends it in `NudgeBegin`; the server turns its own bodies about that point (`ConstructEdits.NudgeBegin`,
`hold.Pivot`). The arc is taken straight after the check's RMB put-back, which waits only for the server's block; the
client's copy was then still 0.1-1.3 mm behind on this PC (measured, 25 runs), and a 120 degree turn about a point d off
the centre moves the centre 2 sin(60) d = 1.7 d. Here that reached 2.2 mm; a slower, loaded CI runner (40 ms lag, 5 %
loss in the check) passes 2.9 mm of lag and 5 mm of swing.

Fix (test only, `SelfTestNudge.cs`): before taking the arc, wait (3 s deadline, a problem if it never comes) until the
client's copy is within 0.5 mm and 0.5 mrad of the server's pose. The 5 mm limit is unchanged. The copy caught up in
0-0.2 s in every run.

Runs, this PC, "Nudge tool" alone: before 25 runs, no arc failure, centre swing up to 2.2 mm (copy lag up to 1.26 mm);
after 25 runs beside six loaded copies, no failure, swing at most 0.7 mm.

Not changed: the pivot is an API (the offline part turns about a pivot off to one side), so the server can't simply use
its own centre. A player who grabs an arc while their copy lags still gets a sub-mm to few-mm swing. A real fix sends the
pivot relative to the first body (a wire change, ProtocolVersion bump): a follow-up for the Nudge tool's owner.

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
