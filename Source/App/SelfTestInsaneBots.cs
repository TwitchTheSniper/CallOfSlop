using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Insane bots" (Source/Game/Bots/Squads): the difficulty's tuning sits above Hard; squads form in
/// team modes only, with sane sizes and roles; the blackboard never learns about an enemy no member could see, and
/// orders follow the last known spot, not the real one; a flanker fires from a wide angle off the suppressor's while
/// the suppressor keeps shooting; a dying member's callout starts a trade; the last member left retreats; a spread-out
/// squad regroups; Insane beats Hard in a clear majority of seeded 4 v 4 matches; the CPU cost at 16 and 32 bots;
/// the same seed plays the same match; and Insane co-op bots survive Zombies rounds 1-3.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> InsaneNotes = new();

    private static void InsaneNote(string s)
    {
        Line(s);
        InsaneNotes.Add(s);
    }

    private static void CheckInsaneBots()
    {
        try
        {
            CheckInsaneTuning();
            CheckInsaneSquadRoster();
            CheckInsaneFlanks();
            CheckInsaneNoCheating();
            CheckInsaneRegroup();
            CheckInsaneDeterminism();
            CheckInsaneWinRate();
            CheckInsaneCpu();
            CheckInsaneZombies();
        }
        finally
        {
            if (InsaneNotes.Count > 0)
                Line("::warning title=Insane bots (results)::" + string.Join("%0A", InsaneNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            World.SetMap(Maps.All[0]);
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private static readonly BotPersonality[] InsaneMix =
        [BotPersonality.Tactician, BotPersonality.Flanker, BotPersonality.Marksman, BotPersonality.Rusher, BotPersonality.Anchor, BotPersonality.Chaotic];

    private static Map InsaneMap(string name, int fallback) => Maps.Find(name) ?? Maps.All[fallback % Maps.All.Length];

    /// <summary>
    /// A seeded Team Deathmatch on <paramref name="map"/>: <paramref name="insane"/> Insane bots on one team and
    /// <paramref name="hard"/> Hard bots on the other, with the same personalities in the same order on both sides.
    /// The host waits (never spawns).
    /// </summary>
    private static Game InsaneVsHard(int seed, Map map, int insane, int hard, bool insaneTeam0, bool squadsOff = false) =>
        Duel(seed, map, BotDifficulty.Insane, insane, BotDifficulty.Hard, hard, insaneTeam0, squadsOff, aFirst: true);

    /// <summary>
    /// A seeded Team Deathmatch: <paramref name="na"/> bots of skill <paramref name="a"/> on one team (team 0 when
    /// <paramref name="aTeam0"/>) and <paramref name="nb"/> of skill <paramref name="b"/> on the other, added in pairs with
    /// the same personality. Join order matters (the later of two otherwise equal bots wins most fights: see the Hard v
    /// Hard control), so pairs alternate which side joins first (A B, B A, A B...), starting with A when
    /// <paramref name="aFirst"/>. The host waits (never spawns).
    /// </summary>
    private static Game Duel(int seed, Map map, BotDifficulty a, int na, BotDifficulty b, int nb, bool aTeam0, bool squadsOff, bool aFirst)
    {
        Game.SeedNextGame = seed;
        World.SetMap(map);
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            SquadsDisabled = squadsOff,
        };
        game.Mode = GameMode.TeamDeathmatch;
        NavGraph.Pump(double.MaxValue);
        var sideA = new HashSet<byte>();
        void Add(bool isA, BotPersonality kind)
        {
            var before = game.Players.Keys.ToHashSet();
            if (game.AddBot(isA ? a : b, kind, quiet: true) && isA)
                sideA.UnionWith(game.Players.Keys.Where(k => !before.Contains(k)));
        }
        for (var i = 0; i < Math.Max(na, nb); i++)
        {
            var kind = InsaneMix[(i + seed) % InsaneMix.Length];
            var first = aFirst == (i % 2 == 0);
            if (first && i < na)
                Add(true, kind);
            if (i < nb)
                Add(false, kind);
            if (!first && i < na)
                Add(true, kind);
        }
        foreach (var p in game.Players.Values.Where(p => p.Bot != null).ToList())
        {
            p.Team = (byte)(sideA.Contains(p.Id) == aTeam0 ? 0 : 1);
            game.SelfTestSpawn(p);
        }
        return game;
    }

    private static void Tick(Game game, ref double now, double seconds, Func<bool>? stop = null) // stop: checked after every tick
    {
        for (var t = 0.0; t < seconds; t += World.TickDt)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (stop?.Invoke() == true)
                return;
        }
    }

    private static bool ClearLine(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var len = d.Length();
        return len < 0.01f || World.Query.Raycast(a, d / len, len, QueryFilter.All) >= len - 0.05f;
    }

    private static Vector3 FlatOf(Vector3 v) => new(v.X, 0, v.Z);

    /// <summary>Degrees between the flat directions from <paramref name="o"/> to <paramref name="a"/> and to <paramref name="b"/>.</summary>
    private static float AngleAt(Vector3 o, Vector3 a, Vector3 b)
    {
        var u = FlatOf(a - o);
        var v = FlatOf(b - o);
        if (u.LengthSquared() < 1e-4f || v.LengthSquared() < 1e-4f)
            return 0;
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(v)), -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>
    /// A squad scenario against the host: three Insane bots (Tactician leader, Marksman suppressor, Flanker) on Blue,
    /// the host on Red with two Red bots kept dead (so the teams count as even and nobody gets moved over).
    /// </summary>
    private static (Game Game, List<Player> Squad) SquadScenario(Map map, int seed)
    {
        Game.SeedNextGame = seed;
        World.SetMap(map);
        var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        game.Mode = GameMode.TeamDeathmatch;
        NavGraph.Pump(double.MaxValue);
        game.AddBot(BotDifficulty.Insane, BotPersonality.Tactician, quiet: true);
        game.AddBot(BotDifficulty.Insane, BotPersonality.Marksman, quiet: true);
        game.AddBot(BotDifficulty.Insane, BotPersonality.Flanker, quiet: true);
        game.AddBot(BotDifficulty.Normal, BotPersonality.Anchor, quiet: true);
        game.AddBot(BotDifficulty.Normal, BotPersonality.Anchor, quiet: true);
        var bots = game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        var squad = bots.Take(3).ToList();
        foreach (var p in squad)
        {
            p.Team = 0;
            game.SelfTestSpawn(p);
        }
        foreach (var p in bots.Skip(3))
        {
            p.Team = 1;
            p.Alive = false;
            p.RespawnAt = 1e9;
        }
        game.Players[0].Team = 1;
        return (game, squad);
    }

    private static Vector3 Centroid(IEnumerable<Player> ps)
    {
        var list = ps.Where(p => p.Alive).ToList();
        return list.Count == 0 ? Vector3.Zero : list.Aggregate(Vector3.Zero, (a, p) => a + p.Position) / list.Count;
    }

    /// <summary>A main-area nav node <paramref name="min"/>-<paramref name="max"/> m from <paramref name="from"/> passing <paramref name="ok"/> (deterministic order).</summary>
    private static Vector3? FindNode(Vector3 from, float min, float max, Func<Vector3, bool> ok, int salt = 0)
    {
        var nav = NavGraph.Current;
        var n = nav.MainNodes.Length;
        if (n == 0)
            return null;
        for (var k = 0; k < Math.Min(n, 1500); k++)
        {
            var q = nav.Nodes[nav.MainNodes[(int)((k * 7919L + salt * 104729L) % n)]];
            var d = FlatOf(q - from).Length();
            if (d >= min && d <= max && ok(q))
                return q;
        }
        return null;
    }

    private static void PlaceHost(Game game, Vector3 at, Vector3 faceTowards)
    {
        var host = game.Players[0];
        var to = faceTowards - at;
        var yaw = MathF.Atan2(to.X, -to.Z);
        game.SelfTestPlace(host, at, yaw);
        host.ImmuneUntil = 1e9;
        game.LookYaw = yaw;
        game.LookPitch = 0;
    }

    // ---- 1. Tuning ----------------------------------------------------------------------------------------------

    private static void CheckInsaneTuning()
    {
        var hard = new BotBrain(BotDifficulty.Hard);
        var insane = new BotBrain(BotDifficulty.Insane);
        InsaneNote($"tuning Hard -> Insane: reaction {hard.Reaction:0.00} -> {insane.Reaction:0.00} s, aim error {hard.AimError:0.000} -> {insane.AimError:0.000} rad, "
                   + $"turn {hard.TurnSpeed:0} -> {insane.TurnSpeed:0} rad/s, settle {hard.Settle:0.0} -> {insane.Settle:0.0}/s, melee wind-up "
                   + $"{Game.BotMeleeWindup(BotDifficulty.Hard):0.00} -> {Game.BotMeleeWindup(BotDifficulty.Insane):0.00} s");
        if (!(insane.Reaction < hard.Reaction && insane.AimError < hard.AimError && insane.TurnSpeed > hard.TurnSpeed && insane.Settle > hard.Settle
              && insane.TriggerDelay <= hard.TriggerDelay && Game.BotMeleeWindup(BotDifficulty.Insane) <= Game.BotMeleeWindup(BotDifficulty.Hard)))
            Fail("Insane isn't at least Hard in every tuning value");
        // The setting saves by name: "Insane" reads back, and the old names still read.
        foreach (var name in new[] { "Easy", "Normal", "Hard", "Insane" })
            if (!Enum.TryParse<BotDifficulty>(name, out var d) || d.ToString() != name)
                Fail($"difficulty '{name}' doesn't round-trip");
        var order = Enum.GetValues<BotDifficulty>();
        if (Array.IndexOf(order, BotDifficulty.Hard) != 2 || Array.IndexOf(order, BotDifficulty.Insane) != 3)
            Fail("BotDifficulty values moved (the lobby's radio buttons use them)");
    }

    // ---- 2. Squads and roles -------------------------------------------------------------------------------------

    private static void CheckInsaneSquadRoster()
    {
        var map = InsaneMap("Crossroads", 2);
        foreach (var (insane, hard) in new[] { (4, 4), (5, 5), (2, 2), (1, 1) })
        {
            using var game = InsaneVsHard(500 + insane, map, insane, hard, insaneTeam0: true);
            var now = 0.0;
            Tick(game, ref now, 2.5);
            var sizes = string.Join("+", game.Squads.Select(s => s.Members.Count));
            var roles = string.Join(" | ", game.Squads.Select(s => string.Join(",", s.Members.Select(m => $"{m.Bot?.Role}:{m.Bot?.Personality}"))));
            Line($"{insane} Insane v {hard} Hard: squads {(sizes.Length > 0 ? sizes : "none")}  {roles}");
            var onTeam = game.Players.Values.Count(p => p.Bot is { Insane: true } && p.Team == 0);
            var want = onTeam >= 2 ? (onTeam + Game.SquadMax - 1) / Game.SquadMax : 0;
            if (game.Squads.Count != want)
                Fail($"{onTeam} Insane bots on a team made {game.Squads.Count} squads, expected {want}");
            if (game.Players.Values.Any(p => p.Bot is { Insane: true } && p.Team != 0))
                Fail("an Insane bot was moved to the other team (the test's teams weren't even)");
            foreach (var s in game.Squads)
            {
                if (s.Members.Count is < 2 or > Game.SquadMax)
                    Fail($"a squad of {s.Members.Count}");
                if (s.Members.Any(m => m.Team != s.Team || m.Bot is not { Insane: true } || m.Bot.Squad != s))
                    Fail("a squad member is on the wrong team, not Insane, or doesn't point back at its squad");
                if (s.Members.Count(m => m.Bot!.Role == SquadRole.Leader) != 1 || s.Leader?.Bot!.Role != SquadRole.Leader)
                    Fail("a squad doesn't have exactly one leader");
                if (s.Members.Count(m => m.Bot!.Role == SquadRole.Flanker) != 1)
                    Fail("a squad doesn't have exactly one flanker");
                if (s.Members.Count >= 3 && s.Members.Count(m => m.Bot!.Role == SquadRole.Suppressor) != 1)
                    Fail("a squad of 3+ doesn't have exactly one suppressor");
            }
            if (game.Players.Values.Any(p => p.Bot is { Skill: BotDifficulty.Hard, Squad: not null }))
                Fail("a Hard bot joined a squad");
        }
        // Free-for-all: Insane bots are Hard+ individuals, no squads.
        using (var ffa = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 7, botSkill: BotDifficulty.Insane,
                   mapId: map.Id) { MapVoteEnabled = false })
        {
            var now = 0.0;
            Tick(ffa, ref now, 2.5);
            Line($"free-for-all with 7 Insane bots: {ffa.Squads.Count} squads");
            if (ffa.Squads.Count != 0 || ffa.Players.Values.Any(p => p.Bot?.Squad != null))
                Fail("squads formed in free-for-all");
        }
    }

    // ---- 3. Suppress + flank, trades, retreat --------------------------------------------------------------------

    private static void CheckInsaneFlanks()
    {
        var angles = new List<float>();
        var flankLines = new List<string>();
        var behind = new List<float>();
        int scenarios = 0, flanked = 0, suppressed = 0, trades = 0, retreats = 0;
        var maps = new[] { InsaneMap("Crossroads", 2), InsaneMap("Ruins", 14), InsaneMap("Office Park", 13), InsaneMap("Quarry", 10) };
        for (var mi = 0; mi < maps.Length; mi++)
        {
            var map = maps[mi];
            var (game, squad) = SquadScenario(map, 900 + mi);
            using var owned = game;
            var host = game.Players[0];
            var now = 0.0;
            Tick(game, ref now, Game.CountdownSeconds + 1.0); // past the countdown (frozen, nobody can be hurt)
            // A spot 20-32 m from the squad that a member can see, with room to go round it on both sides.
            var c = Centroid(squad);
            var nav = NavGraph.Current;
            var spot = FindNode(c, 20f, 32f, q =>
            {
                if (!squad.Any(m => ClearLine(m.Eye, q + new Vector3(0, 1.3f, 0))))
                    return false;
                var axis = Vector3.Normalize(FlatOf(c - q) + new Vector3(1e-3f, 0, 0));
                for (var side = -1; side <= 1; side += 2)
                {
                    var (sin, cos) = MathF.SinCos(side * 1.92f);
                    var dir = new Vector3(axis.X * cos - axis.Z * sin, 0, axis.X * sin + axis.Z * cos);
                    var n = nav.Nearest(q + dir * 10f, needSight: false);
                    if (n < 0 || nav.Component[n] != nav.MainComponent || FlatOf(nav.Nodes[n] - (q + dir * 10f)).Length() > 4.5f)
                        return false;
                }
                return true;
            }, mi);
            if (spot is not { } at)
            {
                Line($"{map.Name}: no spot for the flank scenario (skipped)");
                continue;
            }
            scenarios++;
            PlaceHost(game, at, c);
            var s = game.Squads.FirstOrDefault(q => q.Team == 0);
            if (s == null)
            {
                Fail($"{map.Name}: the three Insane bots formed no squad");
                continue;
            }
            var supp = s.Members.First(m => m.Bot!.Role == SquadRole.Suppressor);
            var flanker = s.Members.First(m => m.Bot!.Role == SquadRole.Flanker);
            var facing = World.FlatForward(host.Yaw);
            double assaultAt = -1, flankShotAt = -1, executeAt = -1;
            double lastSupp = supp.LastShotTime;
            var suppWhileFlanking = 0;
            var suppressShots0 = game.SquadStats.SuppressShots;
            float angle = 0, behindAngle = 0, closest = 999;
            var maxAngleSeen = 0f;
            Tick(game, ref now, 45, () =>
            {
                if (assaultAt < 0 && s.Tactic == SquadTactic.Assault)
                    assaultAt = now;
                if (executeAt < 0 && s.Executing)
                    executeAt = now;
                if (assaultAt >= 0 && flankShotAt < 0)
                {
                    if (supp.LastShotTime > lastSupp)
                        suppWhileFlanking++;
                    if (flanker.Alive)
                    {
                        closest = MathF.Min(closest, FlatOf(flanker.Position - host.Position).Length());
                        if (ClearLine(flanker.Eye, host.Chest))
                            maxAngleSeen = MathF.Max(maxAngleSeen, AngleAt(host.Position, supp.Position, flanker.Position));
                    }
                    if (flanker.LastShotTime > assaultAt)
                    {
                        flankShotAt = now;
                        angle = AngleAt(host.Position, supp.Position, flanker.Position);
                        behindAngle = AngleAt(host.Position, host.Position + facing, flanker.Position);
                    }
                }
                lastSupp = supp.LastShotTime;
                return flankShotAt >= 0 && now - flankShotAt > 1;
            });
            var suppressShots = game.SquadStats.SuppressShots - suppressShots0;
            flankLines.Add($"[{map.Name}: assault {assaultAt:0.0}, push {executeAt:0.0}, shot {flankShotAt:0.0} s, closest {closest:0.0} m, widest {maxAngleSeen:0}, supp {suppWhileFlanking}/{suppressShots}]");
            Line($"{map.Name}: host {FlatOf(at - c).Length():0} m from the squad; assault at {assaultAt:0.0} s, push at {executeAt:0.0} s, flanker's first shot "
                 + (flankShotAt >= 0 ? $"at {flankShotAt:0.0} s from {angle:0} deg off the suppressor ({behindAngle:0} deg off the host's facing)" : $"never (closest {closest:0.0} m, widest view {maxAngleSeen:0} deg)")
                 + $"; suppressor shots while flanking {suppWhileFlanking} (suppressive {suppressShots})");
            if (assaultAt < 0)
            {
                Fail($"{map.Name}: the squad saw the host but never assaulted");
                continue;
            }
            if (flankShotAt >= 0)
            {
                flanked++;
                angles.Add(angle);
                behind.Add(behindAngle);
            }
            if (suppWhileFlanking > 0)
                suppressed++;

            // A callout and a trade: the suppressor dies in view of the host.
            if (supp.Alive)
            {
                var sawKiller = ClearLine(supp.Eye, host.Chest);
                var trades0 = game.SquadStats.Trades;
                game.SelfTestDamageWith(host, supp, 100000, Game.MeleeKillId, now, DamageKind.Melee);
                Tick(game, ref now, 0.2);
                var traded = game.SquadStats.Trades > trades0 && s.Trader is { Alive: true } t && t.Bot!.Order.Kind == SquadOrderKind.Trade;
                Line($"  suppressor killed ({(sawKiller ? "saw" : "didn't see")} the host): trade {(traded ? $"by {s.Trader!.Bot!.Role}" : "none")}");
                if (traded)
                    trades++;
                else if (sawKiller && s.Members.Any(m => m.Alive && FlatOf(m.Position - supp.Position).Length() < 35f))
                    Fail($"{map.Name}: a squadmate died in view of the killer with another member close by, but nobody went to trade");
            }
            // Down to the last member: retreat.
            foreach (var m in s.Members.Where(m => m.Alive && m != flanker).ToList())
                game.SelfTestDamageWith(host, m, 100000, Game.MeleeKillId, now, DamageKind.Melee);
            Tick(game, ref now, 1.2);
            Line($"  last member left: tactic {s.Tactic}");
            if (flanker.Alive)
            {
                if (s.Tactic == SquadTactic.Retreat)
                    retreats++;
                else
                    Fail($"{map.Name}: the last member of a squad of 3 didn't retreat ({s.Tactic})");
            }
        }
        var median = angles.Count > 0 ? angles.OrderBy(a => a).ElementAt(angles.Count / 2) : 0;
        InsaneNote($"flanks: {flanked}/{scenarios} flankers fired, angles off the suppressor {string.Join(", ", angles.Select(a => $"{a:0}"))} deg (median {median:0}), "
                   + $"off the target's facing {string.Join(", ", behind.Select(a => $"{a:0}"))} deg; suppressor fired during the flank in {suppressed}/{scenarios}; "
                   + $"trades {trades}, retreats {retreats}; " + string.Join(" ", flankLines));
        if (scenarios < 2)
            Fail($"only {scenarios} flank scenario(s) could be set up");
        else
        {
            if (flanked * 2 < scenarios)
                Fail($"flankers fired in only {flanked} of {scenarios} scenarios");
            if (angles.Count > 0 && median < 60f)
                Fail($"the median flank angle is {median:0} deg off the suppressor's (want 60+)");
            if (suppressed * 2 < scenarios)
                Fail($"the suppressor kept firing during the flank in only {suppressed} of {scenarios} scenarios");
        }
    }

    // ---- 4. No wallhacks ------------------------------------------------------------------------------------------

    private static void CheckInsaneNoCheating()
    {
        var map = InsaneMap("Office Park", 13);
        var (game, squad) = SquadScenario(map, 1300);
        using var owned = game;
        var host = game.Players[0];
        var now = 0.0;
        Tick(game, ref now, Game.CountdownSeconds + 1.0);
        var s = game.Squads.FirstOrDefault(q => q.Team == 0);
        if (s == null)
        {
            Fail("no squad for the no-cheating check");
            return;
        }
        // 1. Hidden and silent, 40+ m away: any contact about the host must come from a member's real line of sight.
        var hidden = FindNode(Centroid(squad), 40f, 90f, q => squad.All(m => !ClearLine(m.Eye, q + new Vector3(0, 1.2f, 0))), 3);
        if (hidden is not { } h)
        {
            Line("no hidden spot found (skipped)");
            return;
        }
        PlaceHost(game, h, Centroid(squad));
        var lastLos = -100.0;
        var bad = 0;
        var found = -1.0;
        Tick(game, ref now, 15, () =>
        {
            if (squad.Any(m => m.Alive && ClearLine(m.Eye, host.Chest)))
                lastLos = now;
            if (s.Board.TryGet(host.Id, out var c) && c.Seen)
            {
                if (c.Time > lastLos + 0.02 && !c.Callout)
                    bad++;
                if (found < 0)
                    found = now;
            }
            return false;
        });
        Line($"hidden host: {(found >= 0 ? $"found by sight at {found:0.0} s" : "never found")}, contacts without a line of sight: {bad}");
        if (bad > 0)
            Fail($"the squad's blackboard knew where the host was {bad} time(s) without any member having a line of sight");

        // 2. Seen, then gone: the plan follows the last known spot, not the real one.
        var c0 = Centroid(squad);
        var seen = FindNode(c0, 15f, 30f, q => squad.Any(m => m.Alive && ClearLine(m.Eye, q + new Vector3(0, 1.3f, 0))), 4);
        if (seen is not { } v)
        {
            Line("no visible spot found (skipped)");
            return;
        }
        PlaceHost(game, v, c0);
        Tick(game, ref now, 4, () => s.Tactic == SquadTactic.Assault && s.Board.TryGet(host.Id, out _));
        if (!s.Board.TryGet(host.Id, out var before))
        {
            Line("the squad didn't spot the visible host in 4 s (skipped)");
            return;
        }
        var away = FindNode(Centroid(squad), 40f, 120f, q => squad.All(m => !m.Alive || !ClearLine(m.Eye, q + new Vector3(0, 1.2f, 0))), 5);
        if (away is not { } a)
        {
            Line("no hiding spot found (skipped)");
            return;
        }
        PlaceHost(game, a, Centroid(squad));
        var leaks = 0;
        var checks = 0;
        Tick(game, ref now, 2.5, () =>
        {
            if (squad.Any(m => m.Alive && ClearLine(m.Eye, host.Chest)))
                return true; // they legitimately found it again
            checks++;
            if (s.Board.TryGet(host.Id, out var c) && FlatOf(c.Pos - host.Position).Length() < 10f)
                leaks++;
            if (s.Tactic == SquadTactic.Assault && FlatOf(s.TargetAt - host.Position).Length() < 10f)
                leaks++;
            return false;
        });
        Line($"moved the host {FlatOf(a - v).Length():0} m out of sight (last seen {FlatOf(before.Pos - a).Length():0} m from there): "
             + $"the plan stayed on the last known spot ({checks} ticks checked, {leaks} leaks)");
        if (leaks > 0)
            Fail("the squad's plan followed the host's real position after it moved out of sight");
    }

    // ---- 5. Regroup --------------------------------------------------------------------------------------------------

    private static void CheckInsaneRegroup()
    {
        var map = InsaneMap("Crossroads", 2);
        var (game, squad) = SquadScenario(map, 1400);
        using var owned = game;
        var now = 0.0;
        Tick(game, ref now, Game.CountdownSeconds + 3);
        var s = game.Squads.FirstOrDefault(q => q.Team == 0);
        var lead = s?.Leader;
        if (s == null || lead == null)
        {
            Fail("no squad for the regroup check");
            return;
        }
        var regroups0 = game.SquadStats.Regroups;
        var k = 0;
        foreach (var m in s.Members.Where(m => m != lead))
            if (FindNode(lead.Position, 30f, 45f, _ => true, 20 + k++) is { } far)
                game.SelfTestPlace(m, far, 0);
        var spread0 = s.Members.Max(m => FlatOf(m.Position - lead.Position).Length());
        var gathered = -1.0;
        var t0 = now;
        var regrouped = false;
        Tick(game, ref now, 20, () =>
        {
            regrouped |= s.Tactic == SquadTactic.Regroup && now - t0 > 0.1;
            if (regrouped && s.Members.Max(m => FlatOf(m.Position - lead.Position).Length()) < 12f)
                gathered = now - t0;
            return gathered >= 0;
        });
        regrouped |= game.SquadStats.Regroups > regroups0;
        Line($"spread the squad {spread0:0} m from its leader: regroup {(regrouped ? "called" : "NOT called")}, together within 12 m "
             + (gathered >= 0 ? $"after {gathered:0.0} s" : "never (20 s)"));
        InsaneNote($"regroup: spread {spread0:0} m, together after " + (gathered >= 0 ? $"{gathered:0.0} s" : "never"));
        if (!regrouped || gathered < 0)
            Fail("a spread-out squad didn't regroup within 20 s");
    }

    // ---- 6. Determinism ------------------------------------------------------------------------------------------------

    private static ulong InsaneDigest(int seed, Map map)
    {
        using var game = InsaneVsHard(seed, map, 4, 4, insaneTeam0: true);
        var h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (var i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }
        var now = 0.0;
        for (var i = 1; i <= 25 * World.TickRate; i++)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (i % 30 != 0)
                continue;
            foreach (var p in game.Players.Values.OrderBy(p => p.Id))
            {
                Mix((long)MathF.Round(p.Position.X * 100));
                Mix((long)MathF.Round(p.Position.Z * 100));
                Mix(p.Kills);
                Mix(p.Health);
                Mix(p.Bot != null ? (long)p.Bot.Order.Kind : -1);
            }
            foreach (var s in game.Squads)
                Mix((long)s.Tactic);
        }
        return h;
    }

    private static void CheckInsaneDeterminism()
    {
        var map = InsaneMap("Ruins", 14);
        var a = InsaneDigest(4242, map);
        var b = InsaneDigest(4242, map);
        InsaneNote($"determinism: {a:X16} vs {b:X16}");
        if (a != b)
            Fail("the same seeded Insane match played out differently");
    }

    // ---- 7. Insane vs Hard ---------------------------------------------------------------------------------------------

    private static (int Wins, int Draws, int Losses, int ForKills, int AgainstKills, string Detail) InsaneSeries(int count, bool squadsOff, double seconds,
        BotDifficulty a = BotDifficulty.Insane, BotDifficulty b = BotDifficulty.Hard, bool aFirst = true)
    {
        string[] names = ["Courtyard", "Crossroads", "Warehouse 9", "Ruins", "Office Park", "Quarry"];
        int wins = 0, draws = 0, losses = 0, forK = 0, againstK = 0;
        var detail = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var map = InsaneMap(names[i / 2 % names.Length], 1 + i);
            var insaneTeam = i % 2; // every map from both sides
            using var game = Duel(7000 + i, map, a, 4, b, 4, aTeam0: insaneTeam == 0, squadsOff, aFirst);
            var now = 0.0;
            Tick(game, ref now, seconds, () => game.Phase == MatchPhase.Ended);
            int mine = game.TeamScore[insaneTeam], theirs = game.TeamScore[1 - insaneTeam];
            if (game.Phase == MatchPhase.Ended && game.Winner >= Game.TeamWinBase)
            {
                // Ended on the score limit: the winner is what counts.
                var w = game.Winner - Game.TeamWinBase;
                mine = w == insaneTeam ? Math.Max(mine, theirs + 1) : mine;
            }
            forK += mine;
            againstK += theirs;
            if (mine > theirs)
                wins++;
            else if (mine == theirs)
                draws++;
            else
                losses++;
            detail.Add($"{map.Name} {mine}-{theirs}");
        }
        return (wins, draws, losses, forK, againstK, string.Join(", ", detail));
    }

    private static void CheckInsaneWinRate()
    {
        const int matches = 36;
        var sw = Stopwatch.StartNew();
        var (w, d, l, fk, ak, detail) = InsaneSeries(matches, squadsOff: false, seconds: 100);
        InsaneNote($"Insane v Hard, 4 v 4, {matches} seeded 100 s TDM matches (6 maps, both sides): {w} won, {d} drawn, {l} lost; kills {fk}-{ak} ({detail}; {sw.Elapsed.TotalSeconds:0.0} s)");
        if (w * 100 < matches * 65)
            Fail($"Insane won only {w} of {matches} matches against Hard (want 65%+)");
        // Controls (reported only): what the squads add on top of the Hard+ aim, and how fair the setup itself is.
        foreach (var (label, a, b, off, first) in new[]
                 {
                     ("Insane without squads v Hard", BotDifficulty.Insane, BotDifficulty.Hard, true, true),
                     ("Insane v Hard, Hard joining first", BotDifficulty.Insane, BotDifficulty.Hard, false, false),
                     ("Hard v Hard (the fairness control)", BotDifficulty.Hard, BotDifficulty.Hard, false, true),
                     ("Hard v Normal", BotDifficulty.Hard, BotDifficulty.Normal, false, true),
                 })
        {
            sw.Restart();
            var (w2, d2, l2, fk2, ak2, _) = InsaneSeries(24, squadsOff: off, seconds: 100, a, b, first);
            InsaneNote($"{label}, 24 matches: {w2} won, {d2} drawn, {l2} lost; kills {fk2}-{ak2} ({sw.Elapsed.TotalSeconds:0.0} s)");
        }
    }

    // ---- 8. CPU ---------------------------------------------------------------------------------------------------------

    private static void CheckInsaneCpu()
    {
        var map = Maps.All.Where(m => m.MaxPlayers >= Game.MaxPlayers && !m.HasTerrain).OrderBy(m => m.Name).FirstOrDefault()
                  ?? Maps.All.OrderByDescending(m => m.MaxPlayers).First();
        double hardP99 = 0;
        foreach (var (bots, skill) in new[] { (31, BotDifficulty.Hard), (15, BotDifficulty.Insane), (31, BotDifficulty.Insane) })
        {
            Game.SeedNextGame = 31337;
            World.SetMap(map);
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: Math.Min(bots, map.MaxPlayers - 1), botSkill: skill, mapId: map.Id) { MapVoteEnabled = false };
            game.Mode = GameMode.TeamDeathmatch;
            NavGraph.Pump(double.MaxValue);
            Game.SettleMemory();
            var now = 0.0;
            var wall = new List<double>();
            var cpu = new TickCpu();
            var plan0 = 0.0;
            var plans0 = 0;
            for (var i = 0; i < 45 * World.TickRate; i++)
            {
                now += World.TickDt;
                if (i == 3 * World.TickRate)
                {
                    plan0 = game.SquadStats.PlanMs;
                    plans0 = game.SquadStats.Plans;
                }
                cpu.Start();
                var t0 = Stopwatch.GetTimestamp();
                game.Update(now, World.TickDt);
                var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                cpu.Stop(ms, count: i >= 3 * World.TickRate);
                if (i >= 3 * World.TickRate)
                    wall.Add(ms);
            }
            wall.Sort();
            var median = wall[wall.Count / 2];
            var p99 = wall[(int)(wall.Count * 0.99)];
            var planPerTick = (game.SquadStats.PlanMs - plan0) / wall.Count;
            var st = game.SquadStats;
            InsaneNote($"CPU {game.BotCount} {skill} bots on {map.Name}: tick median {median:0.00} ms, p99 {p99:0.00} ms, worst work {cpu.WorstText}; "
                       + $"squads {game.Squads.Count}, planning {planPerTick * 1000:0} us/tick ({st.Plans - plans0} plans), exposure rays {st.ExposureRays}, "
                       + $"assaults {st.Assaults}, pushes {st.Executes}, suppressive shots {st.SuppressShots}, frags {st.Frags}, trades {st.Trades} ({st.TradedKills} traded), "
                       + $"flank kills {st.FlankKills}, regroups {st.Regroups}, retreats {st.Retreats}, smokes {st.Smokes}; "
                       + $"suppress ticks {st.SuppressTicks} / fire {st.SuppressFireTicks} / line {st.SuppressLosTicks}, held-fire ticks {st.HeldFireTicks}");
            if (skill == BotDifficulty.Hard)
            {
                hardP99 = p99;
                continue;
            }
            if (planPerTick > 0.5)
                Fail($"squad planning costs {planPerTick:0.00} ms per tick with {game.BotCount} Insane bots (over 0.5 ms)");
            if (TickCpu.Available && cpu.WorstMs > MaxTickWorkMs)
                Fail($"a tick with {game.BotCount} Insane bots did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
            if (median > MaxTickWorkMs || (p99 > MaxTickWorkMs && hardP99 < MaxTickWorkMs))
                Fail($"ticks with {game.BotCount} Insane bots: median {median:0.00} ms, p99 {p99:0.00} ms (Hard p99 {hardP99:0.00} ms; budget {MaxTickWorkMs} ms)");
            if (bots == 31 && st.Assaults == 0)
                Fail("31 Insane bots never assaulted anyone in 45 s");
        }
    }

    // ---- 9. Zombies co-op ----------------------------------------------------------------------------------------------

    private static void CheckInsaneZombies()
    {
        if (ZTestMap == null)
            return;
        var now = 0.0;
        using var g = ZGame(3, ref now, tweak: r => r.FirstRoundDelay = 2, hostIn: false, skill: BotDifficulty.Insane);
        var t0 = now;
        while (now - t0 < 420 && g.Phase == MatchPhase.Playing && g.ZRound < 4)
        {
            now += World.TickDt;
            g.Update(now, World.TickDt);
        }
        var bots = g.Players.Values.Where(p => p.Bot != null).ToList();
        InsaneNote($"Zombies, 3 Insane co-op bots: round {g.ZRound} after {now - t0:0} s, kills {string.Join("/", bots.Select(b => g.ZP(b).Kills))}, downs {bots.Sum(b => g.ZP(b).Downs)}");
        if (g.ZRound < 4 || g.Phase != MatchPhase.Playing)
            Fail($"three Insane bots didn't survive Zombies rounds 1-3 ({g.ZLastResult})");
        if (g.Squads.Count != 0)
            Fail("squads formed in Zombies (co-op bots cooperate without them)");
    }
}
