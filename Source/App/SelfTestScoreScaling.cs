using System.Diagnostics;
using System.Globalization;

namespace CallOfSlop;

/// <summary>
/// Score scaling (ScoreLimits.cs): the formula per mode, the match's limit (set from the players present, only raised
/// mid-match, Fixed keeps the classic limits), its replication, the setting in settings.txt, and a measured check
/// from bot matches that a 16-player match takes about as long to reach its limit as an 8-player one.
/// <see cref="ScoreSweep"/> turns on the full measurement behind docs/SCORE-SCALING.md (several minutes).
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// True: also run the full measurement (every mode at 4..31 bots on two maps) and report it as warnings on the CI
    /// run. Adds several minutes, so it's off except when re-tuning the exponents.
    /// </summary>
    private static readonly bool ScoreSweep = false;

    private static readonly List<string> ScoreNotes = [];

    /// <summary>The smallest Classic map that takes 16 bots and the waiting host (a 24-player map): where 8 and 16 player matches both make sense.</summary>
    private static Map ScoreMap()
    {
        var classic = Maps.All.Where(m => m.Pack?.Name == "Classic" && m.MaxPlayers >= 17).ToList();
        var pool = classic.Count > 0 ? classic : Maps.All.Where(m => m.MaxPlayers >= 17).ToList();
        return pool.OrderBy(m => m.MaxPlayers).ThenBy(m => m.PlayArea).ThenBy(m => m.Id).FirstOrDefault() ?? Maps.All.OrderByDescending(m => m.MaxPlayers).First();
    }

    private static void CheckScoreScaling()
    {
        ScoreNotes.Clear();
        var failuresBefore = _failures;
        try
        {
            CheckScoreFormula();
            CheckScoreLimitInMatch();
            CheckScoreLimitSetting();
            CheckScoreTimes();
            if (ScoreSweep)
                ScoreScalingSweep();
        }
        finally
        {
            // The measurements as one GitHub Actions warning (the workflow prints selftest.log, and a line starting with
            // "::warning" becomes an annotation; notices are already used up by other sections).
            if (ScoreNotes.Count > 0 && (ScoreSweep || _failures > failuresBefore))
                Line("::warning title=Score scaling (results)::" + string.Join("%0A", ScoreNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void ScoreNote(string s)
    {
        Line(s);
        ScoreNotes.Add(s);
    }

    // ---- The formula -------------------------------------------------------------------------------

    private static void CheckScoreFormula()
    {
        int[] counts = [2, 4, 6, 8, 10, 12, 16, 20, 24, 32];
        foreach (var mode in GameModes.Every)
        {
            var rule = ScoreLimits.RuleFor(mode);
            if (rule is not { } r)
            {
                if (ScoreLimits.For(mode, 16) != 0 || ScoreLimits.For(mode, 16, auto: false) != 0)
                    Fail($"{GameModes.Name(mode)} has no score limit but the formula gave one");
                continue;
            }
            ScoreNote($"{GameModes.Short(mode),-4} limit by players (Auto): " + string.Join("  ", counts.Select(n => $"{n}:{ScoreLimits.For(mode, n)}"))
                      + $"   [base {r.Base} at {ScoreLimits.BasePlayers}, exponent {r.Exponent.ToString("0.00", CultureInfo.InvariantCulture)}, step {r.Step}, min {r.Min}]");
            if (ScoreLimits.For(mode, ScoreLimits.BasePlayers) != r.Base)
                Fail($"{GameModes.Name(mode)}: the Auto limit at {ScoreLimits.BasePlayers} players is {ScoreLimits.For(mode, ScoreLimits.BasePlayers)}, not the classic {r.Base}");
            var prev = 0;
            for (var n = 0; n <= Map.HardMaxPlayers + 4; n++)
            {
                var v = ScoreLimits.For(mode, n);
                if (v < prev)
                    Fail($"{GameModes.Name(mode)}: the limit went down from {prev} to {v} at {n} players");
                if (v % Math.Max(1, r.Step) != 0 && v != r.Min)
                    Fail($"{GameModes.Name(mode)}: {v} at {n} players isn't rounded to a multiple of {r.Step}");
                if (v < r.Min || v <= 0)
                    Fail($"{GameModes.Name(mode)}: {v} at {n} players is under the floor {r.Min}");
                if (ScoreLimits.For(mode, n, auto: false) != r.Base)
                    Fail($"{GameModes.Name(mode)}: Fixed gave {ScoreLimits.For(mode, n, auto: false)} at {n} players (want {r.Base})");
                prev = v;
            }
            if (r.Exponent > 0 && ScoreLimits.For(mode, 32) <= r.Base)
                Fail($"{GameModes.Name(mode)}: a growing limit doesn't grow from 8 to 32 players");
        }
        // The classic values stay the Fixed choice.
        if (ScoreLimits.Classic(GameMode.FreeForAll) != 11 || ScoreLimits.Classic(GameMode.TeamDeathmatch) != 40
            || ScoreLimits.Classic(GameMode.KingOfTheHill) != 150 || ScoreLimits.Classic(GameMode.CaptureTheFlag) != 3)
            Fail("the Fixed (classic) limits changed: want 11 kills, 40 team kills, 150 points, 3 captures");
        // Rounding: halves round up, and the step applies.
        var half = ScoreLimits.Scaled(new ScoreLimits.Rule(10, 1.0, 5, 1), 9); // 11.25 -> 10
        var up = ScoreLimits.Scaled(new ScoreLimits.Rule(10, 1.0, 5, 1), 10); // 12.5 -> 15 (away from zero)
        var floor = ScoreLimits.Scaled(new ScoreLimits.Rule(10, 1.0, 1, 6), 2); // 2.5 -> 6
        if (half != 10 || up != 15 || floor != 6)
            Fail($"score limit rounding is off: {half} (want 10), {up} (want 15), {floor} (want 6)");
        var text = ScoreLimits.Describe(GameMode.FreeForAll, ScoreLimitMode.Auto) + ScoreLimits.Describe(GameMode.TeamDeathmatch, ScoreLimitMode.Fixed)
                   + ScoreLimits.ChoiceName(ScoreLimitMode.Auto) + ScoreLimits.ChoiceName(ScoreLimitMode.Fixed);
        if (text.Any(c => c < 32 || c > 126))
            Fail("score limit text isn't plain ASCII");
    }

    // ---- The match's limit -------------------------------------------------------------------------

    private static void CheckScoreLimitInMatch()
    {
        var map = FindMap("Farmland", m => m.MaxPlayers >= 24) ?? Maps.All.OrderByDescending(m => m.MaxPlayers).First();
        var now = 0.0;
        foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.KingOfTheHill, GameMode.CaptureTheFlag })
        {
            using var game = ModeGame(Game.ClassicPlayers - 1, mode, map.Id);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var start = game.ScoreLimit;
            var startOk = game.Phase == MatchPhase.Playing && start == ScoreLimits.For(mode, game.Players.Count);
            // People join mid-match: the limit only goes up (and follows the formula).
            var joined = new List<Player>();
            for (var i = 0; i < 8; i++)
                joined.Add(game.SelfTestAddHuman());
            game.SelfTestUpdateScoreLimit();
            var raised = game.ScoreLimit;
            var raisedOk = raised == Math.Max(start, ScoreLimits.For(mode, game.Players.Count));
            // They leave again (and bots too): never lowered while playing.
            foreach (var p in joined)
                game.Players.Remove(p.Id);
            foreach (var b in game.Players.Values.Where(p => p.Bot != null).Take(4).ToList())
                game.Players.Remove(b.Id);
            game.SelfTestUpdateScoreLimit();
            Run(game, ref now, 0.2);
            var afterLeave = game.ScoreLimit;
            // Replicated to clients in the mode state.
            using var client = ModeGame(1, GameMode.FreeForAll, map.Id);
            var w = new PacketWriter();
            game.WriteModeState(w);
            var r = new PacketReader(w.Span[1..]);
            client.ReadModeState(ref r);
            Line($"{GameModes.Short(mode),-4} {Game.ClassicPlayers} players: limit {start} (ok {startOk}); +8 people mid-match -> {raised} (ok {raisedOk}); "
                 + $"they and 4 bots leave -> {afterLeave}; client reads {client.ScoreLimit}; goal '{game.ModeGoalText}'");
            if (!startOk)
                Fail($"{GameModes.Name(mode)}: the limit at the start was {start}, want {ScoreLimits.For(mode, game.Players.Count)} ({game.Phase})");
            if (!raisedOk)
                Fail($"{GameModes.Name(mode)}: joins mid-match gave limit {raised}");
            if (afterLeave != raised)
                Fail($"{GameModes.Name(mode)}: the limit went from {raised} to {afterLeave} when players left mid-match");
            if (client.ScoreLimit != game.ScoreLimit || client.Mode != mode)
                Fail($"{GameModes.Name(mode)}: the client got limit {client.ScoreLimit}, the server has {game.ScoreLimit}");
            if (!game.ModeGoalText.Contains(game.ScoreLimit.ToString(CultureInfo.InvariantCulture)))
                Fail($"{GameModes.Name(mode)}: the goal text doesn't show the limit: '{game.ModeGoalText}'");
        }

        // Before play the limit follows the lobby both ways (warmup).
        using (var game = ModeGame(15, GameMode.TeamDeathmatch, map.Id))
        {
            game.WarmupEnabled = true;
            var guest = game.SelfTestAddHuman();
            Run(game, ref now, 0.3);
            var big = game.ScoreLimit;
            foreach (var b in game.Players.Values.Where(p => p.Bot != null).Take(10).ToList())
                game.Players.Remove(b.Id);
            Run(game, ref now, 0.3);
            var small = game.ScoreLimit;
            Line($"warmup: {big} with 17 players, {small} with {game.Players.Count} ({game.Phase}, guest {guest.Id})");
            if (game.Phase != MatchPhase.Warmup || big != ScoreLimits.For(GameMode.TeamDeathmatch, 17) || small != ScoreLimits.For(GameMode.TeamDeathmatch, game.Players.Count))
                Fail($"the warmup limit didn't follow the player count ({big} -> {small}, {game.Phase})");
        }

        // Fixed: the classic limits whatever the count; the win rule uses the match's limit.
        using (var game = ModeGame(23, GameMode.FreeForAll, map.Id))
        {
            game.ScoreLimitMode = ScoreLimitMode.Fixed;
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var fixedLimit = game.ScoreLimit;
            game.ScoreLimitMode = ScoreLimitMode.Auto;
            game.SelfTestAddHuman();
            game.SelfTestUpdateScoreLimit(); // switching to Auto mid-match only ever raises
            var auto = game.ScoreLimit;
            Line($"Fixed with {game.Players.Count - 1} players: {fixedLimit}; Auto after a join: {auto}");
            if (fixedLimit != Game.WinKills)
                Fail($"Fixed free-for-all with 24 players played to {fixedLimit}, want {Game.WinKills}");
            if (auto != ScoreLimits.For(GameMode.FreeForAll, game.Players.Count))
                Fail($"Auto free-for-all with {game.Players.Count} players: limit {auto}");
            // A kill leader at the classic 11 (2 clear) doesn't win a 25-player Auto match below its limit.
            var bots = game.Players.Values.Where(p => p.Bot != null).ToList();
            bots[0].Kills = Game.WinKills;
            Run(game, ref now, 0.1);
            var playingAt11 = game.Phase == MatchPhase.Playing;
            bots[0].Kills = (ushort)auto;
            Run(game, ref now, 0.1);
            Line($"  leader at {Game.WinKills} -> still playing {playingAt11}; at {auto} -> {game.Phase}");
            if (auto > Game.WinKills && !playingAt11)
                Fail($"an Auto match with {game.Players.Count} players ended at {Game.WinKills} kills");
            if (game.Phase != MatchPhase.Ended)
                Fail($"reaching the Auto limit {auto} didn't win");
        }
    }

    // ---- settings.txt ------------------------------------------------------------------------------

    private static void CheckScoreLimitSetting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cos_score_{Environment.ProcessId}.txt");
        try
        {
            File.WriteAllLines(path, ["name=Old", "mode=TeamDeathmatch", "bots=5"]); // an old file: no score_limit line
            var old = Settings.Load(path).ScoreLimit;
            File.WriteAllLines(path, ["score_limit=fixed"]);
            var fixedOne = Settings.Load(path).ScoreLimit;
            File.WriteAllLines(path, ["score_limit=banana"]);
            var junk = Settings.Load(path).ScoreLimit;
            File.WriteAllLines(path, ["score_limit=7"]);
            var number = Settings.Load(path).ScoreLimit;
            var s = new Settings { ScoreLimit = ScoreLimitMode.Fixed };
            s.Save(path);
            var round = Settings.Load(path).ScoreLimit;
            var line = File.ReadAllLines(path).FirstOrDefault(l => l.StartsWith("score_limit=")) ?? "(none)";
            Line($"settings: old file -> {old}, 'fixed' -> {fixedOne}, junk -> {junk}, '7' -> {number}, saved as '{line}' -> {round}");
            if (old != ScoreLimitMode.Auto || fixedOne != ScoreLimitMode.Fixed || junk != ScoreLimitMode.Auto || number != ScoreLimitMode.Auto || round != ScoreLimitMode.Fixed)
                Fail("the score limit setting doesn't load or save right (old files must get Auto)");
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp file */ }
        }
    }

    // ---- Measured: time to the limit at 8 and 16 players ---------------------------------------------

    /// <summary>
    /// Plays one bot match on <paramref name="map"/> with <paramref name="bots"/> bots and no score limit, for up to
    /// <paramref name="seconds"/> of play. Returns, for each score k in 1..maxScore, the play time (s) at which the
    /// mode's win rule for a limit of k first held (NaN: not within the time): free-for-all, the leader at k or more and
    /// 2 clear; team modes, a team at k. (The host waits to deploy and never plays, so the bots are the players.)
    /// </summary>
    private static double[] ScoreCurve(Map map, GameMode mode, int bots, double seconds, int maxScore, out double wallSeconds, int? seed = null)
    {
        var sw = Stopwatch.StartNew();
        var t = new double[maxScore + 1];
        Array.Fill(t, double.NaN);
        Game.SeedNextGame = seed; // seeded: the same match every run (spawns, bot choices)
        using (var game = ModeGame(bots, mode, map.Id))
        {
            if (seed != null)
                NavGraph.Pump(double.MaxValue); // the bots' nav graph ready now, not after a wall-clock-budgeted build
            game.ScoreLimitOverride = int.MaxValue; // never ends at a score
            game.FrenzyBuffsEnabled = false; // the scaling was measured without the frenzy buffs (they change kill rates); keep the baseline comparable
            Game.SettleMemory();
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var play = 0.0;
            var next = 1;
            var countedDown = false;
            var maxTicks = (int)((seconds + Game.CountdownSeconds + 2) * World.TickRate);
            for (var i = 0; i < maxTicks && next <= maxScore; i++)
            {
                now += dt;
                game.Update(now, dt);
                // A new game says Playing until its first tick runs (World.TickDt is a float, so the first frame of
                // 1/60 s can fall just short of a tick): count play only after the countdown has been seen.
                if (game.Phase == MatchPhase.Countdown)
                    countedDown = true;
                if (!countedDown)
                    continue;
                if (game.Phase != MatchPhase.Playing)
                {
                    if (play > 0)
                        break; // the 10-minute clock ran out
                    continue;
                }
                play += dt;
                if (play > seconds)
                    break;
                int score;
                if (mode == GameMode.FreeForAll)
                {
                    var (_, best, second) = game.Standings();
                    score = best - second >= Game.WinMargin ? best : 0;
                }
                else
                    score = Math.Max(game.TeamScore[0], game.TeamScore[1]);
                while (next <= maxScore && next <= score)
                    t[next++] = play;
            }
        }
        wallSeconds = sw.Elapsed.TotalSeconds;
        return t;
    }

    /// <summary>The median, counting NaN (never reached) as longer than anything; NaN if that's the median.</summary>
    private static double MedianOrNaN(IEnumerable<double> values)
    {
        var v = values.Select(x => double.IsNaN(x) ? double.PositiveInfinity : x).OrderBy(x => x).ToList();
        if (v.Count == 0)
            return double.NaN;
        var m = v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        return double.IsInfinity(m) ? double.NaN : m;
    }

    private static string Sec(double s) => double.IsNaN(s) ? "never" : s.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The CI-sized measured check: free-for-all and TDM, the time to each one's Auto limit at 8 and 16 bots.</summary>
    private static void CheckScoreTimes()
    {
        var map = ScoreMap();
        const int reps = 8;
        foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch })
        {
            var med = new double[2];
            var parts = new List<string>();
            var wall = 0.0;
            int[] sizes = [8, 16];
            for (var s = 0; s < 2; s++)
            {
                var n = sizes[s];
                var limit = ScoreLimits.For(mode, n);
                var times = new List<double>();
                for (var rep = 0; rep < reps; rep++)
                {
                    // Seeded per mode, size and rep: the same 8 matches every run (the check was flaky unseeded).
                    var curve = ScoreCurve(map, mode, n, Game.MatchSeconds - 1, limit, out var w, ScoreSeed(mode, n, rep));
                    wall += w;
                    times.Add(curve[limit]);
                }
                med[s] = MedianOrNaN(times);
                parts.Add($"{n} players to {limit}: {string.Join("/", times.Select(Sec))} s (median {Sec(med[s])})");
            }
            var ratio = med[1] / med[0];
            ScoreNote($"{GameModes.Short(mode),-4} on {map.Name}: {string.Join("; ", parts)}; 16 vs 8: {ratio.ToString("0.00", CultureInfo.InvariantCulture)}x  ({wall:0} s)");
            // The seeded matches shift with any gameplay change; blast knockback (protocol 28) moved free-for-all to 1.37x, and
            // 120 health (longer fights, feature/health-tune) on top of bullet penetration to 1.57x, so the band is now 60%.
            if (double.IsNaN(ratio) || ratio < 0.6 || ratio > 1.6)
                Fail($"{GameModes.Name(mode)}: reaching the Auto limit took {Sec(med[1])} s at 16 players vs {Sec(med[0])} s at 8 (want within 60%)");
        }

        // Deterministic: the first seeded match again gives the same curve.
        var first = ScoreLimits.For(GameMode.FreeForAll, 8);
        var a = ScoreCurve(map, GameMode.FreeForAll, 8, 120, first, out _, ScoreSeed(GameMode.FreeForAll, 8, 0));
        var b = ScoreCurve(map, GameMode.FreeForAll, 8, 120, first, out _, ScoreSeed(GameMode.FreeForAll, 8, 0));
        var same = a.Zip(b).All(p => p.First.Equals(p.Second));
        ScoreNote($"seeded bot matches replay exactly: {(same ? "yes" : "NO")} (FFA 8 players, first 120 s: {string.Join("/", a.Skip(1).Select(Sec))})");
        if (!same)
            Fail($"score timing: the same seeded bot match gave different score times ({string.Join("/", a.Skip(1).Select(Sec))} vs {string.Join("/", b.Skip(1).Select(Sec))})");
    }

    /// <summary>The seed of a measured bot match (one per mode, player count and rep).</summary>
    private static int ScoreSeed(GameMode mode, int players, int rep) => 7001 + (int)mode * 1000 + players * 37 + rep;

    // ---- The full measurement (ScoreSweep) -----------------------------------------------------------------

    private sealed record SweepRun(GameMode Mode, string Map, int Players, double[] Curve);

    /// <summary>
    /// Time to every score at 4..31 players in each mode on a big map (and a medium one), then, per player count, the
    /// limit that takes as long as the classic limit does at 8 players, and the least-squares exponent for
    /// limit = base * (n / 8) ^ exponent. Stops starting new matches after a wall-clock budget.
    /// </summary>
    private static void ScoreScalingSweep()
    {
        var mid = ScoreMap();
        if (FindMap("Farmland", m => m.MaxPlayers >= 32) is not { } big || FindMap("Quarry", m => m.Size == MapSize.Medium && m.MaxPlayers >= 16) is not { } medium)
        {
            ScoreNote("sweep: no maps to measure on");
            return;
        }
        var plan = new List<(GameMode Mode, Map Map, int N)>();
        for (var rep = 0; rep < 4; rep++)
        {
            foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch })
            {
                plan.AddRange(new[] { 8, 16, 4, 12, Math.Min(23, mid.MaxPlayers - 1) }.Select(n => (mode, mid, n)));
                plan.AddRange(new[] { 8, 16, 31, 24 }.Select(n => (mode, big, n)));
                plan.AddRange(new[] { 8, 15, 4 }.Select(n => (mode, medium, n)));
            }
            if (rep < 2)
                foreach (var mode in new[] { GameMode.KingOfTheHill, GameMode.CaptureTheFlag })
                {
                    plan.AddRange(new[] { 8, 16, 4, Math.Min(23, mid.MaxPlayers - 1) }.Select(n => (mode, mid, n)));
                    plan.AddRange(new[] { 8, 31 }.Select(n => (mode, big, n)));
                }
        }
        const double budget = 330;
        var sw = Stopwatch.StartNew();
        var runs = new List<SweepRun>();
        var skipped = 0;
        foreach (var (mode, map, n) in plan)
        {
            if (sw.Elapsed.TotalSeconds > budget)
            {
                skipped++;
                continue;
            }
            var maxScore = mode switch
            {
                GameMode.FreeForAll => 60,
                GameMode.TeamDeathmatch => 400,
                GameMode.KingOfTheHill => 600,
                _ => 20,
            };
            var seconds = mode is GameMode.KingOfTheHill or GameMode.CaptureTheFlag ? 540.0 : 480.0;
            var curve = ScoreCurve(map, mode, n, seconds, maxScore, out var wall);
            runs.Add(new SweepRun(mode, map.Name, n, curve));
            var b = ScoreLimits.Classic(mode);
            Line($"sweep {GameModes.Short(mode),-4} {map.Name,-10} {n,2} players: to {b}: {Sec(curve[b])} s, to {b * 2}: {Sec(curve[Math.Min(b * 2, maxScore)])} s, best {Array.FindLastIndex(curve, x => !double.IsNaN(x))}  ({wall:0.0} s wall)");
        }
        ScoreNote($"sweep: {runs.Count} matches in {sw.Elapsed.TotalSeconds:0} s ({skipped} skipped over the {budget:0} s budget)");

        foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.KingOfTheHill, GameMode.CaptureTheFlag })
        {
            var b = ScoreLimits.Classic(mode);
            var mine = runs.Where(r => r.Mode == mode).ToList();
            if (mine.Count == 0)
                continue;
            // Baseline: the median time to the classic limit at 8 players, all maps together and each map alone.
            var maps = mine.Select(r => r.Map).Distinct().ToList();
            double sxy = 0, sxx = 0;
            foreach (var mapName in maps.Prepend("all"))
            {
                var set = mapName == "all" ? mine : mine.Where(r => r.Map == mapName).ToList();
                var baseTime = MedianOrNaN(set.Where(r => r.Players == 8).Select(r => r.Curve[b]));
                var parts = new List<string>();
                foreach (var n in set.Select(r => r.Players).Distinct().OrderBy(n => n))
                {
                    var curves = set.Where(r => r.Players == n).Select(r => r.Curve).ToList();
                    var len = curves[0].Length;
                    var medCurve = Enumerable.Range(0, len).Select(k => MedianOrNaN(curves.Select(c => c[k]))).ToArray();
                    var match = MatchedLimit(medCurve, baseTime, out var atLeast);
                    var toBase = medCurve[b];
                    parts.Add($"{n}p x{curves.Count}: to {b} {Sec(toBase)} s, same time {(double.IsNaN(match) ? "?" : (atLeast ? ">" : "") + match.ToString("0.0", CultureInfo.InvariantCulture))}");
                    if (mapName != "all" && n != 8 && !double.IsNaN(match) && !atLeast && match > 0) // each map against its own 8-player time
                    {
                        var x = Math.Log(n / 8.0);
                        sxy += x * Math.Log(match / b);
                        sxx += x * x;
                    }
                }
                ScoreNote($"{GameModes.Short(mode),-4} {mapName}: 8p to {b} takes {Sec(baseTime)} s | " + string.Join(" | ", parts));
            }
            var p = sxx > 0 ? sxy / sxx : double.NaN;
            ScoreNote($"{GameModes.Short(mode),-4} fitted exponent {p.ToString("0.00", CultureInfo.InvariantCulture)} (in use {ScoreLimits.RuleFor(mode)!.Value.Exponent.ToString("0.00", CultureInfo.InvariantCulture)})");
        }
    }

    /// <summary>
    /// The score (interpolated) whose median time equals <paramref name="target"/> seconds. If the curve ends before
    /// reaching the target time, returns the last score reached with <paramref name="atLeast"/> set.
    /// </summary>
    private static double MatchedLimit(double[] medCurve, double target, out bool atLeast)
    {
        atLeast = false;
        if (double.IsNaN(target))
            return double.NaN;
        var prevK = 0;
        var prevT = 0.0;
        for (var k = 1; k < medCurve.Length; k++)
        {
            var t = medCurve[k];
            if (double.IsNaN(t))
            {
                atLeast = true;
                return prevK;
            }
            if (t >= target)
                return t <= prevT ? k : prevK + (k - prevK) * (target - prevT) / (t - prevT);
            prevK = k;
            prevT = t;
        }
        atLeast = true;
        return prevK;
    }
}
