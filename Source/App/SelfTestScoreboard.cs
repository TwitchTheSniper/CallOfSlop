using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test for the scoreboard (Tab): no health column in any mode, a Level column for people and bots, prestige shown
/// like the lobby, the level reaching clients (and updating mid-match without anything in the snapshots), and no
/// allocation in the scoreboard's per-frame model (ScoreboardModel.cs).
/// </summary>
public static partial class SelfTest
{
    private static readonly RakNetGUID ScoreGuid = new() { G = 0x5C0, SystemIndex = 0 };

    private static void CheckScoreboardLevel()
    {
        ScoreboardColumnsCheck();
        ScoreboardLevelCells();
        ScoreboardLevelReplication();
        ScoreboardNoAllocation();
    }

    // ---- Columns: Level in, HP out, in every mode, both layouts ----
    private static void ScoreboardColumnsCheck()
    {
        var bad = 0;
        foreach (var m in GameModeRegistry.All)
            foreach (var compact in new[] { false, true })
            {
                var heads = ScoreboardModel.Headers(m, compact);
                var levels = heads.Count(h => h == ScoreboardModel.LevelHeader);
                var health = heads.Count(h => h.Contains("HP", StringComparison.OrdinalIgnoreCase) || h.Contains("Health", StringComparison.OrdinalIgnoreCase));
                if (levels != 1 || health != 0 || heads.Length != 6 || !ReferenceEquals(heads, ScoreboardModel.Headers(m, compact)))
                {
                    bad++;
                    Fail($"scoreboard: {m.Name} {(compact ? "compact" : "full")} columns \"{string.Join(" | ", heads)}\" (want one Level, no HP, 6 columns, cached)");
                }
            }
        var ffa = GameModeRegistry.Get(GameMode.FreeForAll);
        Line($"columns: {GameModeRegistry.All.Count} modes x 2 layouts, {bad} wrong; full \"{string.Join(" | ", ScoreboardModel.Headers(ffa, false))}\", compact \"{string.Join(" | ", ScoreboardModel.Headers(ffa, true))}\"");
    }

    // ---- Level cells: people (prestige like the lobby), yourself (live), bots, unknown ----
    private static void ScoreboardLevelCells()
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2)
            { Progress = MaxedProgress(3) };
        var host = game.Local!;
        var bot = game.Players.Values.First(p => p.Bot != null);
        var hostCell = ScoreboardModel.Level(game, host);
        var botCell = ScoreboardModel.Level(game, bot);
        var wantHost = Progress.RankLabel(3, Progress.MaxLevel);
        Line($"cells: host \"{hostCell.Label}\" (hat {hostCell.Hat}), bot \"{botCell.Label}\", P2 L13 \"{ScoreboardModel.LevelLabel(2, 13)}\", L7 \"{ScoreboardModel.LevelLabel(0, 7)}\", unknown \"{ScoreboardModel.LevelLabel(0, 0)}\"");
        if (hostCell.Label != wantHost || hostCell.Bot)
            Fail($"scoreboard: the host's Level cell is \"{hostCell.Label}\" (want \"{wantHost}\", the lobby's label)");
        if (botCell.Label != ScoreboardModel.BotLevel || !botCell.Bot || botCell.Hat != HatId.None)
            Fail($"scoreboard: a bot's Level cell is \"{botCell.Label}\" (hat {botCell.Hat}; want BOT, no hat)");
        if (ScoreboardModel.LevelLabel(2, 13) != "P2 L13" || ScoreboardModel.LevelLabel(0, 7) != "L7" || ScoreboardModel.LevelLabel(0, 0) != ScoreboardModel.UnknownLevel)
            Fail("scoreboard: level labels must read like the lobby (\"P2 L13\", \"L7\", \"-\" when unknown)");
        if (!ReferenceEquals(ScoreboardModel.LevelLabel(2, 13), ScoreboardModel.LevelLabel(2, 13)))
            Fail("scoreboard: level labels aren't cached");
        // A person who isn't you: from the roster fields, with their hat beside it.
        game.ServerSetHat(host, HatId.Fedora);
        game.Progress = null; // as seen by someone else: the roster's prestige and level
        host.Prestige = 2;
        host.Level = 13;
        var other = ScoreboardModel.Level(game, host);
        if (other.Label != "P2 L13" || other.Hat != HatId.Fedora)
            Fail($"scoreboard: a P2 L13 player wearing the Fedora shows \"{other.Label}\" with {other.Hat}");
        // Status never shows health.
        host.Health = 73;
        foreach (var compact in new[] { false, true })
        {
            var s = ScoreboardModel.Status(host, compact);
            if (s.Contains("73") || s.Contains("HP", StringComparison.OrdinalIgnoreCase))
                Fail($"scoreboard: the status cell shows health (\"{s}\")");
        }
    }

    // ---- Replication: Hello -> roster; a level-up comes with the next loadout packet ----
    private static byte[] ProfileLoadoutPacket(HatId hat, byte? prestige, byte? level)
    {
        var w = new PacketWriter();
        w.Begin(Msg.Loadout).U8(Weapons.DefaultPrimary.Id).U8(Weapons.DefaultSecondary.Id).U8(0).U8(0).U32(0).U32(0)
            .U8(0).U8(0).U8(0).U8((byte)KillStreaks.Starters[0]).U8((byte)KillStreaks.Starters[1]).U8((byte)KillStreaks.Starters[2])
            .U8((byte)hat);
        if (prestige is { } p && level is { } l)
            w.U8(p).U8(l);
        return w.Span.ToArray();
    }

    private static void ScoreboardLevelReplication()
    {
        var savedMap = World.Map;
        var savedPhysics = World.PhysicsMode;
        try
        {
            using var server = new Game(NetPeer.Offline(), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
            server.Progress = new Progress { ReadOnly = true };
            server.SelfTestPacketRaw(ScoreGuid, [(byte)DefaultMessageIDTypes.NewIncomingConnection], 0);
            var guest = server.Players.Values.First(p => p.Bot == null && p.Id != server.LocalId);
            var bot = server.Players.Values.First(p => p.Bot != null);
            var w = new PacketWriter();
            w.Begin(Msg.Hello).U8(5).Bytes("Guest"u8).U8(1).U8(5);
            server.SelfTestPacket(ScoreGuid, w.Span, 0);
            var atJoin = (guest.Prestige, guest.Level);

            // Levels up to P2 L13 during the match: the next respawn's loadout packet carries it.
            server.SelfTestPacket(ScoreGuid, ProfileLoadoutPacket(HatId.BaseballCap, 2, 13), 0);
            var afterUp = (guest.Prestige, guest.Level, guest.Hat);
            // An older client (hat byte only) keeps what it had.
            server.SelfTestPacket(ScoreGuid, ProfileLoadoutPacket(HatId.BaseballCap, null, null), 0);
            var older = (guest.Prestige, guest.Level);
            // A bot can't be given a level.
            server.ServerUpdateProfile(bot, 5, 50);

            var roster = server.SelfTestRosterPacket();
            using var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            client.SelfTestPacket(default, roster, 0);
            var seen = client.ProfileOf(guest.Id);
            var seenBot = client.ProfileOf(bot.Id);
            Line($"replication: join P{atJoin.Prestige} L{atJoin.Level}; loadout with P2 L13 -> P{afterUp.Prestige} L{afterUp.Level} ({afterUp.Hat}); older client -> P{older.Prestige} L{older.Level}; "
                 + $"client sees guest P{seen.Prestige} L{seen.Level}, bot P{seenBot.Prestige} L{seenBot.Level} (roster {roster.Length} bytes)");
            if (atJoin != (1, 5))
                Fail($"scoreboard replication: the Hello gave P{atJoin.Prestige} L{atJoin.Level} (want P1 L5)");
            if (afterUp != (2, 13, HatId.BaseballCap))
                Fail($"scoreboard replication: a mid-match level-up gave P{afterUp.Prestige} L{afterUp.Level} {afterUp.Hat} (want P2 L13, the Baseball Cap unlocked at P2)");
            if (older != (2, 13))
                Fail($"scoreboard replication: a loadout without the profile bytes changed the level to P{older.Prestige} L{older.Level}");
            if (seen != (2, 13) || seenBot != (0, 0) || bot.Level != 0 || bot.Prestige != 0)
                Fail($"scoreboard replication: the client sees guest P{seen.Prestige} L{seen.Level} (want P2 L13), bot P{seenBot.Prestige} L{seenBot.Level} (want none)");
        }
        finally
        {
            if (World.Map != savedMap)
                World.SetMap(savedMap);
            World.PhysicsMode = savedPhysics;
        }
    }

    // ---- The per-frame model allocates nothing once warm ----
    private static void ScoreboardNoAllocation()
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 7)
            { Progress = MaxedProgress(1) };
        var ranked = new List<Player>(64);
        var left = new List<Player>(64);
        var right = new List<Player>(64);
        var modules = GameModeRegistry.All;
        var sink = 0;
        void Frame()
        {
            ScoreboardModel.Rank(game, ranked);
            ScoreboardModel.Split(game, ranked, left, right);
            sink += ScoreboardModel.BotCount(game) + ScoreboardModel.UpperName(game.Module).Length + ScoreboardModel.TeamUpper[1].Length;
            for (var c = 0; c < 2; c++)
                sink += ScoreboardModel.Headers(game.Module, c == 1).Length;
            for (var i = 0; i < ranked.Count; i++)
            {
                var p = ranked[i];
                var cell = ScoreboardModel.Level(game, p);
                sink += cell.Label.Length + ScoreboardModel.Status(p, false).Length + ScoreboardModel.Status(p, true).Length + ScoreboardModel.Num(i + 1).Length;
                for (var m = 0; m < modules.Count; m++)
                {
                    var (a, b) = modules[m].ScoreboardCells(game, p);
                    sink += a.Length + b.Length;
                }
            }
        }
        Frame(); // warm: caches filled, JIT done
        Frame();
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var bytes = SteadyAllocBytes(() =>
        {
            for (var f = 0; f < 200; f++)
                Frame();
        }, warmups: 0);
        Line($"allocation: 200 scoreboard frames for {ranked.Count} players across {modules.Count} modes' cells: {bytes} bytes (checksum {sink})");
        if (ranked.Count != game.Players.Count || left.Count + right.Count != ranked.Count)
            Fail($"scoreboard: the ranking has {ranked.Count} of {game.Players.Count} players ({left.Count} + {right.Count} in the columns)");
        if (bytes > 0)
            Fail($"scoreboard: the per-frame scoreboard model allocated {bytes} bytes over 200 frames (want 0)");
    }
}
