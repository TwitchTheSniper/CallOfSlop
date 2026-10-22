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

    /// <summary>The matches digested by <see cref="ModeMatchDigest"/>: mode, bots, map, seed, seconds and the digest recorded before the refactor (0: not recorded yet).</summary>
    private static readonly (GameMode Mode, int Bots, byte MapId, int Seed, double Seconds, ulong Digest)[] ModeDigests =
    [
        (GameMode.FreeForAll, 7, 0, 4101, 20, 0),
        (GameMode.TeamDeathmatch, 7, 0, 4102, 20, 0),
        (GameMode.KingOfTheHill, 7, 1, 4103, 20, 0),
        (GameMode.CaptureTheFlag, 7, 1, 4104, 20, 0),
        (GameMode.GunGame, 7, 0, 4105, 20, 0),
        (GameMode.Zombies, 2, 0, 4106, 20, 0),
    ];

    private static void CheckModeModules()
    {
        CheckModeDigests();
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
            Line($"{GameModes.Name(mode),-17} {bots} bots, map {mapId}, seed {seed}, {seconds:0} s: digest {a:X16} ({summary}; {sw.Elapsed.TotalSeconds:0.0} s)");
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
