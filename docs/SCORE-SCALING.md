# Score limits that scale with the player count

User request: with larger player counts the score limit should go up, so bigger matches take about as long to finish
even though more people are earning points.

Code: `Source/Game/ScoreLimits.cs` (formula, the match's limit), `Source/App/SelfTestScoreScaling.cs` (checks and the
measurement), lobby option in `Source/UI/UiModes.cs` (`ScoreLimitPicker`), setting `score_limit` in `Settings.cs`.

## Rules

- **Base:** the classic limits were tuned for 8 players (you + 7 bots, `Game.ClassicPlayers`): free-for-all 11 kills
  (win by 2), Team Deathmatch 40 team kills, King of the Hill 150 points, Capture the Flag 3 captures. Gun Game (a
  per-player ladder) and Zombies (no limit) don't scale.
- **Formula (Auto):** `limit(N) = max(Min, round(Base * (n / 8) ^ Exponent / Step) * Step)`, `n = clamp(N, 2, 32)`,
  N = everyone in the match (people + bots, including people still waiting to deploy). Halves round up. The limit at
  8 players is always the classic one, and the limit never goes down as N goes up.
- **When N is measured:** during warmup and the countdown the limit follows the lobby both ways. At GO it is set from
  everyone present. While playing it can only go **up** (people joining mid-match; a kill-feed line says so: "17
  players now: the score limit goes up to 17 kills"), never down when people leave, so nobody suddenly wins.
- **Fixed:** the classic limits whatever the count. (Before this change TDM already scaled crudely: 5 kills per player,
  40 to 100. Fixed is the plain 40.)
- **HUD:** unchanged layout, the number is the match's limit ("First to 15 kills, win by 2", "first to 60 kills").
- **Network:** the mode state's last U16 (protocol 11: the TDM limit) is now the current mode's limit: protocol 20.
- **Setting:** host / single player option "Score limit: Auto (scales with players) / Fixed" under the game mode,
  saved as `score_limit=auto|fixed` in settings.txt; files without the line get Auto.

## Expected shape per mode (why the exponents differ)

- **Free-for-all:** a single player's kill rate goes up with more players on the same map (more targets nearby), and
  the leader is the best of N, so the first player to X arrives sooner as N grows. Expect an exponent well below 1.
- **Team Deathmatch:** team kills per minute = (N / 2) x the per-player kill rate, which itself rises with density.
  Expect an exponent of about 1 or a bit more.
- **King of the Hill:** points come one a second whoever holds the hill alone; more players mostly means more
  contesting. Expect about 0 (no scaling).
- **Capture the Flag:** captures need a carrier to cross the map; more defenders and more attackers roughly cancel.
  Expect about 0.

## Formulas in use

| Mode | Base (8 players) | Exponent | Step | Min | 4 | 8 | 12 | 16 | 24 | 32 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Free-for-all | 11 | 0.60 | 1 | 7 | 7 | 11 | 14 | 17 | 21 | 25 |
| Team Deathmatch | 40 | 1.00 | 5 | 20 | 20 | 40 | 60 | 80 | 120 | 160 |
| King of the Hill | 150 | 0.00 | 10 | 150 | 150 | 150 | 150 | 150 | 150 | 150 |
| Capture the Flag | 3 | 0.00 | 1 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |

**Status: provisional.** These are first estimates from the reasoning above. The measurement below could not run
yet: GitHub Actions refused every job on 2026-10-21 ("recent account payments have failed or your spending limit
needs to be increased"), and there is no .NET SDK in the agent container. The exponents must be replaced with the
measured ones (`ScoreLimits.cs`) once the sweep has run.

## Measurement

`SelfTestScoreScaling.cs` measures with the self-test's offline bot matches (simulated as fast as the CPU allows, 60
ticks a second of game time, Normal bots):

- `ScoreCurve(map, mode, bots, seconds, maxScore)` plays one match with **no score limit**
  (`Game.ScoreLimitOverride = int.MaxValue`) and records, for every possible limit k, the play time at which the
  mode's win rule for k first held (free-for-all: leader at k or more and 2 clear; team modes: a team at k). One match
  therefore gives the time-to-limit for every candidate limit. The host waits to deploy and never plays, so the bots
  are the N players.
- **Sweep** (`ScoreSweep = true`, about 5 extra minutes of CI): free-for-all and TDM at 4 / 8 / 12 / 16 / 23 players on
  the smallest Classic 24-player map, 8 / 16 / 24 / 31 on Farmland (32) and 4 / 8 / 15 on Quarry (16), twice each;
  King of the Hill and CTF once at 4 / 8 / 16 / 23 and 8 / 31. A 330 s wall-clock budget stops it starting new
  matches. For each player count it reports the median time to the classic limit, the limit that takes as long as
  the classic limit does at 8 players (interpolated on the median curve), and a least-squares exponent fitted on
  log(limit / base) against log(n / 8). The results appear as one "Score scaling (results)" warning on the CI run.
- **Every run** (CI-sized check): free-for-all and TDM on the 24-player map, 4 matches each at 8 and 16 players, each
  until its Auto limit; the median time at 16 must be within 35% of the median at 8.

### Measurements

Pending (see Status). Fill in from the CI warning: per mode, per N, median time to the classic limit and the
same-time limit, and the fitted exponent.

| Mode | Map | N | Matches | Time to classic limit (s) | Same-time limit |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

To re-measure: set `ScoreSweep = true` in `SelfTestScoreScaling.cs`, push, read the warning, put the fitted
exponents in `ScoreLimits.cs` (rounded to 0.05), update the table above, and set `ScoreSweep = false` again.
