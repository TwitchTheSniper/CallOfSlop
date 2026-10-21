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
- **Network:** the mode state's last U16 (protocol 11: the TDM limit) is now the current mode's limit: protocol 21.
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

| Mode | Base (8 players) | Exponent | Step | Min | 2 | 4 | 6 | 8 | 10 | 12 | 16 | 20 | 24 | 32 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Free-for-all | 11 | 0.85 | 1 | 7 | 7 | 7 | 9 | 11 | 13 | 16 | 20 | 24 | 28 | 36 |
| Team Deathmatch | 40 | 0.80 | 5 | 20 | 20 | 25 | 30 | 40 | 50 | 55 | 70 | 85 | 95 | 120 |
| King of the Hill | 150 | 0 | 10 | 150 | 150 | 150 | 150 | 150 | 150 | 150 | 150 | 150 | 150 | 150 |
| Capture the Flag | 3 | 0 | 1 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 | 3 |

## Measurement

`SelfTestScoreScaling.cs` measures with the self-test's offline bot matches (simulated as fast as the CPU allows, 60
ticks a second of game time, Normal bots):

- `ScoreCurve(map, mode, bots, seconds, maxScore)` plays one match with **no score limit**
  (`Game.ScoreLimitOverride = int.MaxValue`) and records, for every possible limit k, the play time at which the
  mode's win rule for k first held (free-for-all: leader at k or more and 2 clear; team modes: a team at k). One match
  therefore gives the time-to-limit for every candidate limit. The host waits to deploy and never plays, so the bots
  are the N players.
- **Sweep** (`ScoreSweep = true`, about 2 minutes of CI): free-for-all and TDM at 4 / 8 / 12 / 16 / 23 players on
  Canyon (the smallest Classic 24-player map), 8 / 16 / 24 / 31 on Farmland (32) and 4 / 8 / 15 on Quarry (16), four
  matches each; King of the Hill and CTF twice at 4 / 8 / 16 / 23 on Canyon and 8 / 31 on Farmland. Per map and
  player count: the median curve, the median time to the classic limit, and the **same-time limit** (the score whose
  median time equals that map's 8-player time to the classic limit, interpolated). The exponent is a least-squares fit
  of log(same-time limit / base) against log(N / 8), each map against its own 8-player time.
- **Every run** (CI-sized check, about 10 s): free-for-all and TDM on Canyon, 8 matches each at 8 and 16 players, each
  until its Auto limit; the median time at 16 must be within 35% of the median at 8.

### Measurements (CI run 39694006161, 2026-10-21, 120 matches in 108 s)

Median of 4 matches per row (2 for KotH / CTF). "Same-time limit": the limit that takes as long at N as the classic
limit at 8 on that map. "never": not reached within the time (8 min FFA / TDM, 9 min KotH / CTF).

| Mode | Map | N | Time to classic limit (s) | Same-time limit |
| --- | --- | --- | --- | --- |
| FFA | Canyon (24) | 4 | 104 | 5.2 |
| FFA | Canyon | 8 | 60 | 11 |
| FFA | Canyon | 12 | 40 | 16.3 |
| FFA | Canyon | 16 | 35 | 17.9 |
| FFA | Canyon | 23 | 27 | 27.8 |
| FFA | Quarry (16) | 4 | 54 | 9.9 |
| FFA | Quarry | 8 | 49 | 11 |
| FFA | Quarry | 15 | 31 | 18.2 |
| FFA | Farmland (32) | 8 | 161 | 11 |
| FFA | Farmland | 16 | 63 | 22.4 |
| FFA | Farmland | 24 | 41 | 38.6 |
| FFA | Farmland | 31 | 42 | 46.3 |
| TDM | Canyon | 4 | 311 | 24.6 |
| TDM | Canyon | 8 | 199 | 40 |
| TDM | Canyon | 12 | 92 | 71.7 |
| TDM | Canyon | 16 | 98 | 77.4 |
| TDM | Canyon | 23 | 92 | 94.7 |
| TDM | Quarry | 4 | 160 | 22.6 |
| TDM | Quarry | 8 | 100 | 40 |
| TDM | Quarry | 15 | 57 | 73.1 |
| TDM | Farmland | 8 | 337 | 40 |
| TDM | Farmland | 16 | 204 | 65.5 |
| TDM | Farmland | 24 | 148 | 86.9 |
| TDM | Farmland | 31 | 104 | 106.2 |
| KotH | Farmland | 8 / 31 | 440 / never | 150 / ~60 |
| KotH | Canyon | 4 / 8 / 16 / 23 | 391 / never / never / 360 | - |
| CTF | both | 4 to 31 | never except 4 players on Canyon (351) | - |

**Fits:** free-for-all 0.94 over all three maps, 0.77 without Farmland (8 bots on a 32-player map is sparse, so it
speeds up more than a real lobby would); chosen **0.85**. Team Deathmatch 0.79 over all three maps, 0.87 without
Farmland; chosen **0.80**. King of the Hill: 8 bots take 7+ minutes to 150 and bigger matches are no faster (more
contesting), so no scaling (the rules never lower a limit for more players). Capture the Flag: bots almost never get
3 captures in 9 minutes, at any size, so there's no trend to fit; no scaling.

**Check with the chosen formula:** the 8 vs 16 player check on Canyon passed with the earlier estimates (FFA 0.88x,
TDM 0.98x); the final numbers are in the report of the run that turned the sweep off.

To re-measure: set `ScoreSweep = true` in `SelfTestScoreScaling.cs`, push, read the "Score scaling (results)"
warning on the run, put the fitted exponents in `ScoreLimits.cs`, update the tables above, and set it back to false.
