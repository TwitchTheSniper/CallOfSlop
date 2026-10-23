using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test section for the game mode modules (Source/Game/Modes): the registry, each module's rules, state round trips, bot matches.</summary>
public static partial class SelfTest
{
    /// <summary>
    /// A deterministic digest of a seeded bot match in one mode: every second, everyone's position, kills, deaths, team
    /// and gun level, the team scores, the hill, the flags and the phase. The same build must give the same digest
    /// every run; the recorded values (<see cref="ModeDigests"/>) are what the match looked like before the modes were
    /// made modular, so the refactor can be checked to have changed nothing.
    /// </summary>
    private static ulong ModeMatchDigest(GameMode mode, int bots, byte mapId, int seed, double seconds, out string summary)
    {
        Game.SeedNextGame = seed;
        var zombies = mode == GameMode.Zombies;
        var map = zombies ? ZTestMap! : Maps.Get(mapId);
        World.SetMap(map);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: bots, mapId: map.Id)
        {
            MapVoteEnabled = false,
            FrenzyBuffsEnabled = false, // the digests were recorded before the frenzy buffs (which change how fights go); the frenzy self-test covers them
        };
        if (zombies)
        {
            game.ZSeed = (uint)seed;
            game.ZHostSitsOut = true;
        }
        game.Mode = mode;
        NavGraph.Pump(double.MaxValue);
        var h = 14695981039346656037UL; // FNV-1a 64
        void Mix(long v)
        {
            for (var i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }
        var now = 0.0;
        var ticks = (int)Math.Round(seconds * World.TickRate);
        for (var i = 1; i <= ticks; i++)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (i % World.TickRate != 0)
                continue;
            foreach (var p in game.Players.Values.OrderBy(p => p.Id))
            {
                Mix(p.Id);
                Mix((long)MathF.Round(p.Position.X * 100));
                Mix((long)MathF.Round(p.Position.Y * 100));
                Mix((long)MathF.Round(p.Position.Z * 100));
                Mix(p.Kills);
                Mix(p.Deaths);
                Mix(p.Alive ? 1 : 0);
                Mix(p.Team);
                Mix(p.GunLevel);
                Mix(p.Health);
            }
            Mix(game.TeamScore[0]);
            Mix(game.TeamScore[1]);
            Mix(game.HillIndex);
            Mix(game.HillOwner);
            Mix(game.HillContested ? 1 : 0);
            foreach (var f in game.Flags)
            {
                Mix(f.Carrier);
                Mix(f.Home ? 1 : 0);
                Mix((long)MathF.Round(f.Position.X * 100));
                Mix((long)MathF.Round(f.Position.Z * 100));
            }
            Mix((long)game.Phase);
            Mix(game.ScoreLimit);
            if (zombies)
            {
                Mix(game.ZRound);
                Mix(game.ZRemaining);
                Mix(game.AliveZombies);
                foreach (var z in game.Horde)
                {
                    Mix((long)MathF.Round(z.Position.X * 100));
                    Mix((long)MathF.Round(z.Position.Z * 100));
                }
            }
        }
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        summary = zombies
            ? $"round {game.ZRound}, {game.AliveZombies} zombies alive, {game.Horde.Count} in the horde, {game.Phase}"
            : $"kills {kills}, team score {game.TeamScore[0]}-{game.TeamScore[1]}, hill {game.HillIndex}/{game.HillOwner}, top gun {game.Players.Values.Max(p => (int)p.GunLevel) + 1}, {game.Phase}";
        return h;
    }

    // Re-recorded for FFA, TDM, KOTH and CTF with the tactical equipment (bots carry and use tactical items; GameTactical.cs), on top of main 8d6dfa4.
    /// <summary>The matches digested by <see cref="ModeMatchDigest"/>: mode, bots, map, seed, seconds and the digest recorded before the refactor (0: not recorded yet).</summary>
    private static readonly (GameMode Mode, int Bots, byte MapId, int Seed, double Seconds, ulong Digest)[] ModeDigests =
    [
        // Re-recorded when bot shots stopped rewinding later-updated players to a stale history slot (SelfTestLagComp.cs),
        // and again (all but Zombies) when the kill streaks grew from 7 to 15: bots draw their three random streaks from the bigger pool.
        // All six re-recorded when max health went from 100 to 120 with the faster ramping regen (feature/health-tune),
        // on top of bullet penetration (KotH/CTF had been re-recorded for it on main, SelfTestPenetration.cs) and the grenade-throw fix.
        // FFA / KotH / CTF again when bot snipers started carrying their scope (slower to aim and move; SelfTestSniperScopes.cs),
        // and all four team/FFA matches on top of main b6594c5 (main's re-record without the scopes: FFA B89C7675, TDM 607E5D0D, KotH B8F46D77, CTF 09DADA4C).
        (GameMode.FreeForAll, 7, 0, 4101, 20, 0xB269F244E4FE3775),
        (GameMode.TeamDeathmatch, 7, 0, 4102, 20, 0x53346F4122D4CC97),
        (GameMode.KingOfTheHill, 7, 1, 4103, 20, 0x0EDF29DF2B72A36A),
        (GameMode.CaptureTheFlag, 7, 1, 4104, 20, 0x2B74CFD49C25E46A),
        (GameMode.GunGame, 7, 0, 4105, 20, 0x6580734EF1194E21),
        (GameMode.Zombies, 2, 0, 4106, 20, 0xAEA5EBE353E956D9),
    ];

    private static readonly List<string> ModeNotes = new();

    /// <summary>A result line, also kept for the section's GitHub annotation (so the numbers can be read from a green run).</summary>
    private static void ModeNote(string s)
    {
        Line(s);
        ModeNotes.Add(s);
    }

    private static void CheckModeModules()
    {
        try
        {
            CheckModeRegistry();
            CheckModeCapabilities();
            CheckModeStateRoundTrips();
            CheckModeBotMatches();
            CheckModeDigests();
        }
        finally
        {
            if (ModeNotes.Count > 0)
                Line("::warning title=Mode modules (results)::" + string.Join("%0A", ModeNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    // ---- The registry: every mode, stable ids, where each shows up --------------------------------------------

    private static void CheckModeRegistry()
    {
        var all = GameModeRegistry.All;
        ModeNote($"{all.Count} modes registered: {string.Join(", ", all.Select(m => $"{m.Name} ({m.Short}, id {(byte)m.Id}{(m.DevOnly ? ", dev" : "")})"))}");
        // The wire ids are fixed: the shipped six in order, then the template.
        var expected = new[]
        {
            (GameMode.FreeForAll, 0, typeof(FreeForAllModule)), (GameMode.TeamDeathmatch, 1, typeof(TeamDeathmatchModule)),
            (GameMode.KingOfTheHill, 2, typeof(KingOfTheHillModule)), (GameMode.CaptureTheFlag, 3, typeof(CaptureTheFlagModule)),
            (GameMode.GunGame, 4, typeof(GunGameModule)), (GameMode.Zombies, 5, typeof(ZombiesModule)), (GameMode.LastManStanding, 6, typeof(LastManStandingModule)),
            (GameMode.Coop, 7, typeof(CoopModule)), (GameMode.TestRange, 8, typeof(TestRangeModule)),
        };
        foreach (var (id, wire, type) in expected)
        {
            if ((byte)id != wire)
                Fail($"{id} is {(byte)id} on the wire, not {wire}");
            if (GameModeRegistry.Get(id).GetType() != type || !GameModeRegistry.IsKnown(id))
                Fail($"{id} isn't registered as {type.Name}");
        }
        if (all.Select(m => m.Id).Distinct().Count() != all.Count)
            Fail("two modules share a mode id");
        if (!GameModes.Every.SequenceEqual(new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.KingOfTheHill, GameMode.CaptureTheFlag, GameMode.GunGame, GameMode.Zombies, GameMode.Coop, GameMode.TestRange }))
            Fail($"GameModes.Every changed: {string.Join(",", GameModes.Every)}");
        if (GameModeRegistry.Get((GameMode)200).Id != GameMode.FreeForAll || GameModeRegistry.IsKnown((GameMode)200))
            Fail("an unknown mode byte didn't fall back to free-for-all");
        // Every piece of player-visible text is ASCII.
        foreach (var m in all)
            foreach (var text in new[] { m.Name, m.Short, m.Rules(), m.Rules(17), m.Description, m.MapWarning, m.ScoreUnit, m.ScoreboardColumns.First, m.ScoreboardColumns.Second })
                if (text.Any(c => c > 126 || c < 32))
                    Fail($"{m.Name}: non-ASCII text '{text}'");
        // Where modes show up: random map / a plain map -> the five PvP modes and Test Range; Zombies Test -> Zombies and the Dev template too.
        var plain = GameModes.ForMap(null);
        var courtyard = GameModes.ForMap(Maps.All[0]);
        var ztest = ZTestMap != null ? GameModes.ForMap(ZTestMap) : Array.Empty<GameMode>();
        ModeNote($"random map: {string.Join(",", plain.Select(GameModes.Short))}; {Maps.All[0].Name}: {string.Join(",", courtyard.Select(GameModes.Short))}; Zombies Test: {string.Join(",", ztest.Select(GameModes.Short))}");
        var plainWant = GameModes.All.Append(GameMode.TestRange).ToArray();
        if (!plain.SequenceEqual(plainWant) || !courtyard.SequenceEqual(plainWant))
            Fail("a plain map should offer exactly the five PvP modes and Test Range");
        if (ZTestMap != null && (!ztest.Contains(GameMode.Zombies) || !ztest.Contains(GameMode.LastManStanding) || ztest.Length != plainWant.Length + 2))
            Fail("Zombies Test should offer Zombies and the Dev-only template on top of the PvP modes");
        if (!GameModes.Fits(GameMode.LastManStanding, Maps.All[0]) || GameModes.Fits(GameMode.Zombies, Maps.All[0]) || (ZTestMap != null && !GameModes.Fits(GameMode.Zombies, ZTestMap)))
            Fail("Fits is wrong for Zombies or the template");
        // The server info carries the template's id like any other.
        var info = new ServerInfo("Modes", "Courtyard", 2, 8, 1, MatchPhase.Warmup, 60123, ServerInfo.ProtocolVersion, GameMode.LastManStanding);
        if (ServerInfo.Find(info.Encode()) != info)
            Fail("the template mode didn't survive the server info");
    }

    // ---- Capability flags: the same rules the game had before the modes were modules -------------------------------

    private static void CheckModeCapabilities()
    {
        // (mode, team, loadout, streaks, fixed perks, co-op, map vote, time limit, score rule, unit)
        var want = new (GameMode Mode, bool Team, bool Loadout, bool Streaks, PerkSet? Perks, bool CoOp, bool Vote, bool Clock, int Classic, string Unit)[]
        {
            (GameMode.FreeForAll, false, true, true, null, false, true, true, 11, "kills"),
            (GameMode.TeamDeathmatch, true, true, true, null, false, true, true, 40, "kills"),
            (GameMode.KingOfTheHill, true, true, true, null, false, true, true, 150, "points"),
            (GameMode.CaptureTheFlag, true, true, true, null, false, true, true, 3, "captures"),
            (GameMode.GunGame, false, false, false, GunGame.Perks, false, true, true, 0, "kills"),
            (GameMode.Zombies, false, false, false, null, true, false, false, 0, "kills"),
            (GameMode.LastManStanding, false, true, true, null, false, true, true, 0, "kills"),
            (GameMode.Coop, false, true, false, null, true, false, false, 0, "kills"),
            (GameMode.TestRange, false, true, true, null, false, false, false, 0, "kills"),
        };
        foreach (var w in want)
        {
            var m = GameModeRegistry.Get(w.Mode);
            var flags = $"team {m.TeamBased} loadout {m.UsesLoadout} streaks {m.AllowsStreaks} perks {(m.FixedPerks != null ? "fixed" : "own")} co-op {m.CoOp} vote {m.UsesMapVote} clock {m.HasTimeLimit} limit {ScoreLimits.Classic(w.Mode)} {m.ScoreUnit}";
            Line($"{m.Name,-17} {flags}");
            if (m.TeamBased != w.Team || m.UsesLoadout != w.Loadout || m.AllowsStreaks != w.Streaks || !Equals(m.FixedPerks, w.Perks) || m.CoOp != w.CoOp
                || m.UsesMapVote != w.Vote || m.HasTimeLimit != w.Clock || ScoreLimits.Classic(w.Mode) != w.Classic || m.ScoreUnit != w.Unit)
                Fail($"{m.Name}: capability flags changed ({flags})");
            if (m.DevOnly != (w.Mode == GameMode.LastManStanding))
                Fail($"{m.Name}: DevOnly is {m.DevOnly}");
        }
        // Through a game: the derived rules match the flags.
        using (var g = ModeGame(3, GameMode.GunGame))
        {
            if (!g.SkipsLoadout || g.ModeAllowsStreaks || g.IsTeamMode || !g.AreEnemies(g.Players[0], g.Players[1]))
                Fail("Gun Game through a game: loadout / streaks / teams / enemies");
        }
        using (var g = ModeGame(3, GameMode.TeamDeathmatch))
        {
            var a = g.Players[0];
            var mate = g.Players.Values.First(p => p != a && p.Team == a.Team);
            var foe = g.Players.Values.First(p => p.Team != a.Team);
            if (g.SkipsLoadout || !g.ModeAllowsStreaks || !g.IsTeamMode || g.AreEnemies(a, mate) || !g.AreEnemies(a, foe) || g.SideColor(a) != GameModes.TeamColors[a.Team])
                Fail("Team Deathmatch through a game: loadout / streaks / teams / enemies / colours");
        }
        if (ZTestMap != null)
        {
            var now = 0.0;
            using var z = ZGame(1, ref now);
            var a = z.Players[0];
            var b = z.Players.Values.First(p => p != a);
            if (!z.SkipsLoadout || z.ModeAllowsStreaks || z.IsTeamMode || z.AreEnemies(a, b) || z.SideColor(a) != GameModes.TeamColors[0] || z.MapMaxPlayers != Game.ZombiesMaxPlayers)
                Fail("Zombies through a game: loadout / streaks / allies / colours / 4 players");
        }
        using (var g = ModeGame(3, GameMode.LastManStanding))
        {
            if (g.SkipsLoadout || !g.ModeAllowsStreaks || g.IsTeamMode || !g.AreEnemies(g.Players[0], g.Players[1]) || g.Module.CanRespawn(g, g.Players[1], 1))
                Fail("Last Man Standing through a game: loadout / streaks / enemies / one life");
        }
        Line("derived rules (SkipsLoadout, ModeAllowsStreaks, IsTeamMode, AreEnemies, SideColor, MapMaxPlayers) follow the flags");
    }

    // ---- The mode state: each module's own tail round-trips; other versions and short tails are skipped safely ----------

    private static void CheckModeStateRoundTrips()
    {
        foreach (var mode in GameModeRegistry.All.Select(m => m.Id))
        {
            if (mode == GameMode.Zombies && ZTestMap == null)
                continue;
            var map = mode == GameMode.Zombies ? ZTestMap!.Id : (byte)1;
            using var server = ModeGame(3, mode, map);
            using var client = ModeGame(3, GameMode.FreeForAll, map);
            server.TeamScore[0] = 2;
            server.TeamScore[1] = 1;
            server.Players[1].GunLevel = 7;
            string expect;
            Func<Game, string> actual;
            switch (server.Module)
            {
                case KingOfTheHillModule k:
                    k.HillIndex = 1;
                    k.HillOwner = 1;
                    k.HillContested = true;
                    k.HillMoveIn = 12.5;
                    expect = "1/1/True/12.5";
                    actual = g => { var c = g.ModuleOf<KingOfTheHillModule>(); return $"{c.HillIndex}/{c.HillOwner}/{c.HillContested}/{c.HillMoveIn}"; };
                    break;
                case CaptureTheFlagModule f:
                    f.Flags[1].Carrier = 0;
                    f.Flags[1].Home = false;
                    f.Flags[1].Position = new Vector3(3, 0, 4);
                    f.Flags[0].ReturnIn = 7;
                    expect = "0/False/<3, 0, 4>/7";
                    actual = g => { var c = g.ModuleOf<CaptureTheFlagModule>(); return $"{c.Flags[1].Carrier}/{c.Flags[1].Home}/{c.Flags[1].Position}/{c.Flags[0].ReturnIn}"; };
                    break;
                case LastManStandingModule l:
                    l.Standing = 3;
                    expect = "3";
                    actual = g => $"{g.ModuleOf<LastManStandingModule>().Standing}";
                    break;
                default:
                    expect = "";
                    actual = _ => "";
                    break;
            }
            var w = new PacketWriter();
            server.WriteModeState(w);
            var r = new PacketReader(w.Span[1..]);
            client.ReadModeState(ref r);
            var got = actual(client);
            var shared = client.Mode == mode && client.TeamScore[0] == 2 && client.TeamScore[1] == 1 && client.Players[1].GunLevel == 7
                         && server.Players.Values.All(p => client.Players[p.Id].Team == p.Team) && client.ScoreLimit == server.ScoreLimit;
            ModeNote($"{GameModes.Name(mode),-17} mode state {w.Length} bytes (tail v{server.Module.StateVersion}), shared part {shared}, tail '{got}'");
            if (!shared)
                Fail($"{GameModes.Name(mode)}: the shared mode state didn't replicate");
            if (got != expect)
                Fail($"{GameModes.Name(mode)}: the mode's own state didn't round-trip ('{got}', want '{expect}')");
            if (r.Remaining != 0)
                Fail($"{GameModes.Name(mode)}: {r.Remaining} byte(s) of the mode state weren't read");

            // A tail from another version of the module is skipped, not misread; a truncated tail is dropped without a throw.
            var bytes = w.Span.ToArray();
            if (expect.Length > 0)
            {
                // The tail's version byte sits 3 bytes before the tail; the two bytes after it hold the tail's length.
                var tailStart = -1;
                for (var i = bytes.Length - 3; i >= 0; i--)
                    if (bytes[i] == server.Module.StateVersion && (bytes[i + 1] | bytes[i + 2] << 8) == bytes.Length - i - 3)
                    {
                        tailStart = i;
                        break;
                    }
                if (tailStart < 0)
                {
                    Fail($"{GameModes.Name(mode)}: couldn't find the mode tail in the packet");
                    continue;
                }
                var other = (byte[])bytes.Clone();
                other[tailStart] = (byte)(server.Module.StateVersion + 1);
                using var fresh = ModeGame(3, GameMode.FreeForAll, map);
                var before = actual(fresh);
                var r2 = new PacketReader(other.AsSpan(1));
                fresh.ReadModeState(ref r2);
                if (actual(fresh) != before || fresh.Mode != mode)
                    Fail($"{GameModes.Name(mode)}: a tail of another version was read anyway");
                var cut = bytes[..(bytes.Length - 1)];
                var r3 = new PacketReader(cut.AsSpan(1));
                try
                {
                    fresh.ReadModeState(ref r3);
                }
                catch (MalformedPacketException ex)
                {
                    Fail($"{GameModes.Name(mode)}: a truncated tail threw: {ex.Message}");
                }
            }
        }
    }

    // ---- A short bot match in every registered mode, through the registry (the template included) ---------------------

    private static void CheckModeBotMatches()
    {
        var map = Maps.All.FirstOrDefault(m => m.Size == MapSize.Small && m.Id > 0) ?? Maps.All[0];
        foreach (var proto in GameModeRegistry.All)
        {
            var mode = proto.Id;
            if (mode == GameMode.Zombies && ZTestMap == null)
                continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var zombies = mode == GameMode.Zombies;
            var seconds = mode == GameMode.LastManStanding ? 60.0 : 20.0;
            Game game;
            var now = 0.0;
            Game.SeedNextGame = 6100 + (int)mode * 17; // seeded: the same match every run (a flaky check is no check)
            if (zombies)
                game = ZGame(2, ref now, hostIn: false);
            else if (mode == GameMode.Coop && Maps.Find("Silo") is { } silo)
            {
                ClutteredLargeNav(silo); // co-op plays on a map with a mission (Silo Breach): 3 co-op bots against the director's squads
                game = ModeGame(3, mode, silo.Id);
            }
            else
                game = ModeGame(Game.ClassicPlayers - 1, mode, map.Id);
            Game.SeedNextGame = null; // (ZGame may not have made a Game with it)
            // (No NavGraph.Pump here: the recorded digests below were made after these matches built the graph in budgeted
            // slices, and pumping it whole changes what they replay.)
            using (game)
            {
                var startAlive = game.Players.Values.Count(p => p.Alive);
                var respawned = false;
                try
                {
                    const double dt = 1.0 / World.TickRate;
                    for (var t = 0.0; t < seconds; t += dt)
                    {
                        now += dt;
                        var deathsBefore = game.Players.Values.Sum(p => (int)p.Deaths);
                        game.Update(now, dt);
                        if (mode == GameMode.LastManStanding && game.Phase == MatchPhase.Playing && deathsBefore > 0
                            && game.Players.Values.Count(p => p.Alive) + game.Players.Values.Sum(p => (int)p.Deaths) > game.Players.Count)
                            respawned = true;
                        foreach (var p in game.Players.Values)
                            if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                                throw new Exception($"player {p.Id} position is NaN");
                        if (game.Phase == MatchPhase.Ended)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail($"{proto.Name}: bot match crashed: {ex}");
                    continue;
                }
                var kills = game.Players.Values.Sum(p => (int)p.Kills);
                var alive = game.Players.Values.Count(p => p.Alive);
                var verdict = game.Module.SelfTestVerify(game);
                ModeNote($"{proto.Name,-17} on {World.Map.Name}: {sw.Elapsed.TotalSeconds:0.0} s wall, kills {kills}, alive {alive}/{startAlive}, team score {game.TeamScore[0]}-{game.TeamScore[1]}, phase {game.Phase}" +
                     (game.Phase == MatchPhase.Ended ? $" ({game.MatchResult})" : "") + (verdict != null ? $" PROBLEM: {verdict}" : ""));
                if (game.Module.GetType() != proto.GetType())
                    Fail($"{proto.Name}: the game ran {game.Module.Name} instead");
                if (verdict != null)
                    Fail($"{proto.Name}: {verdict}");
                if (mode == GameMode.LastManStanding)
                {
                    if (respawned)
                        Fail("Last Man Standing: someone respawned mid-match");
                    if (kills == 0)
                        Fail("Last Man Standing: no kills in 60 s of bot play");
                    if (game.Phase == MatchPhase.Ended && game.Winner == Game.NoWinner && !game.MatchResult.StartsWith("Draw"))
                        Fail($"Last Man Standing: ended without a winner: {game.MatchResult}");
                }
            }
        }
    }

    /// <summary>Seeded bot matches in every mode give the digests recorded before the refactor (and the same digest twice in a row).</summary>
    private static void CheckModeDigests()
    {
        var baseline = new List<string>();
        foreach (var (mode, bots, mapId, seed, seconds, want) in ModeDigests)
        {
            if (mode == GameMode.Zombies && ZTestMap == null)
                continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a = ModeMatchDigest(mode, bots, mapId, seed, seconds, out var summary);
            var b = ModeMatchDigest(mode, bots, mapId, seed, seconds, out _);
            ModeNote($"{GameModes.Name(mode),-17} {bots} bots, map {mapId}, seed {seed}, {seconds:0} s: digest {a:X16} ({summary}; {sw.Elapsed.TotalSeconds:0.0} s)");
            if (a != b)
                Fail($"{GameModes.Name(mode)}: the same seeded match gave two digests ({a:X16} vs {b:X16})");
            if (want == 0)
                baseline.Add($"{mode}=0x{a:X16}");
            else if (a != want)
                Fail($"{GameModes.Name(mode)}: a seeded match plays differently than before the refactor (digest {a:X16}, recorded {want:X16}): {summary}");
        }
        if (baseline.Count > 0)
            Fail($"BASELINE (not a bug: record these in SelfTestModeModules.cs): {string.Join(" ", baseline)}");
    }
}
