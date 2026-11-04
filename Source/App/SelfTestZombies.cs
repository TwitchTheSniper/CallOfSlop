using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{
    private static Map? ZTestMap => Maps.Find("Zombies Test");

    /// <summary>A Zombies game on Zombies Test (offline), live and playing, with the given rules tweaks.</summary>
    private static Game ZGame(int bots, ref double now, uint seed = 1234, Action<ZombiesRules>? tweak = null, bool hostIn = true, BotDifficulty skill = BotDifficulty.Normal, Map? onMap = null,
        Action<Game>? setup = null)
    {
        var map = onMap ?? ZTestMap!;
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: bots, botSkill: skill, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        tweak?.Invoke(rules);
        g.ZRules = rules;
        g.ZSeed = seed;
        g.ZHostSitsOut = !hostIn;
        g.Mode = GameMode.Zombies;
        setup?.Invoke(g); // (before the run starts: the start round, the test kit)
        Run(g, ref now, Game.CountdownSeconds + 0.2);
        if (hostIn)
        {
            g.RequestRespawn();
            Run(g, ref now, 0.1);
        }
        return g;
    }

    private static void ZTicks(Game g, ref double now, double seconds)
    {
        for (var t = 0.0; t < seconds; t += World.TickDt)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
        }
    }

    private static readonly List<string> ZNotes = new();

    /// <summary>A result line, also kept for the section's one GitHub notice (the report's numbers).</summary>
    private static void ZLine(string s)
    {
        Line(s);
        ZNotes.Add(s);
    }
}
