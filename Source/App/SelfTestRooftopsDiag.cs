using System.Numerics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

// TEMPORARY diagnostic for the flaky Rooftops far walk (fix/rooftops-flake). Removed before merging.
public static partial class SelfTest
{
    private static string G(Vector3 v) => $"({v.X:0.0},{v.Y:0.0},{v.Z:0.0})";

    private static void CheckRooftopsDiag()
    {
        var search = new NavSearch();
        var path = new int[128];
        var rng = new Random(7);
        // Reproduce the far-walk pair picks (ProtoBox, Courtyard, Rooftops) exactly as CheckBots does.
        Vector3 from = default, to = default;
        NavGraph g = NavGraph.Empty;
        foreach (var name in new[] { "ProtoBox", "Courtyard", "Rooftops" })
        {
            if (Maps.Find(name) is not { } m)
                continue;
            World.SetMap(m);
            for (var wait = 0; wait < 1000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50);
            g = NavGraph.Current;
            var len = 0;
            for (var attempt = 0; attempt < 40 && len == 0; attempt++)
            {
                var a = g.MainNodes[rng.Next(g.MainNodes.Length)];
                var b = g.MainNodes[rng.Next(g.MainNodes.Length)];
                if (Vector3.Distance(g.Nodes[a], g.Nodes[b]) < MathF.Min(m.HalfX, m.HalfZ))
                    continue;
                len = search.Find(g, a, b, path);
                from = g.Nodes[a];
                to = g.Nodes[b];
            }
            Line($"diag {name}: pair {G(from)} -> {G(to)}, graph {Hash(g):x8} nodes {g.Nodes.Length} edges {g.EdgeCount} lastResorts {g.LastResorts} rejected {g.WalkRejected}");
        }
        var map = Maps.Find("Rooftops")!;
        World.SetMap(map);
        var h0 = Hash(NavGraph.Current);
        var h1 = Hash(NavGraph.Build(map));
        var h2 = Hash(NavGraph.Build(map));
        var info = new StringBuilder();
        info.Append($"Rooftops graph hashes pump {h0:x8} build {h1:x8} build {h2:x8}; pair {G(from)} -> {G(to)}. ");
        // Edges that cross a building's outer wall line near the start building (x 8..22, z -21..-9).
        info.Append("Wall-crossing edges near (15,-15): ");
        var crossing = 0;
        for (var i = 0; i < g.Nodes.Length; i++)
            for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
            {
                var a = g.Nodes[i];
                var b = g.Nodes[g.EdgeTo[e]];
                bool InB(Vector3 p) => p.X > 8 && p.X < 22 && p.Z > -21 && p.Z < -9 && p.Y < 3;
                if (InB(a) == InB(b) || MathF.Max(a.Y, b.Y) > 3)
                    continue;
                crossing++;
                if (crossing <= 40)
                    info.Append($"{G(a)}>{G(b)} f{g.EdgeFlags[e]} c{g.EdgeCost[e]:0.0} L{G(g.EdgeLedge[e])}; ");
            }
        info.Append($"total {crossing}.");
        Fail("DIAG " + info);

        // The walk, many times, each with its own seeded game.
        var fails = new List<string>();
        var results = new StringBuilder();
        var nFail = 0;
        for (var k = 0; k < 40; k++)
        {
            Game.SeedNextGame = 1000 + k;
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
                bots: 1, botSkill: BotDifficulty.Normal, mapId: map.Id);
            game.MapVoteEnabled = false;
            var bot = game.Players.Values.First(p => p.Bot != null);
            World.SetMap(map);
            var nav = NavGraph.Current;
            bot.Teleport(from);
            bot.Bot!.ForcedGoal = to;
            var route = 52f;
            var limit = route / 4f + 8f;
            const double dt = 1.0 / World.TickRate;
            var now = 0.0;
            var took = -1.0;
            var trace = new StringBuilder();
            var paths = new List<string>();
            var lastSig = "";
            for (var i = 0; i < limit * World.TickRate; i++)
            {
                now += dt;
                game.Update(now, dt);
                var b = bot.Bot;
                var sig = b.PathLen == 0 ? "" : $"{b.PathLen}:{b.Path[0]}:{b.Path[Math.Min(2, b.PathLen - 1)]}";
                if (sig != lastSig && sig != "" && paths.Count < 4)
                {
                    lastSig = sig;
                    var ps = new StringBuilder($"t{now:0.00} at {G(bot.Position)} path:");
                    for (var q = 0; q < Math.Min(b.PathLen, 6); q++)
                    {
                        var fl = q == 0 ? -1 : FlagsBetween(nav, b.Path[q - 1], b.Path[q]);
                        ps.Append($" {(fl >= 0 ? "-f" + fl + "-" : "")}{G(nav.Nodes[b.Path[q]])}");
                    }
                    paths.Add(ps.ToString());
                }
                if (i % (World.TickRate / 2) == 0 && now < 12)
                    trace.Append($"t{now:0.0} {G(bot.Position)} {b.PathIdx}/{b.PathLen} e{(b.CurEdge >= 0 ? nav.EdgeFlags[b.CurEdge] : -1)} m{bot.Sim.MantleTicks} s{b.StuckCount} ss{b.StuckSecs:0.0} {b.Mode}; ");
                if (Vector3.Distance(bot.Position, to) < 2.5f)
                {
                    took = now;
                    break;
                }
            }
            results.Append(took >= 0 ? $"{k}:{took:0.0} " : $"{k}:FAIL ");
            if (took < 0)
            {
                nFail++;
                if (fails.Count < 4)
                    fails.Add($"seed {1000 + k} {bot.Bot.Personality} ended {G(bot.Position)} {bot.Bot.PathIdx}/{bot.Bot.PathLen} | PATHS {string.Join(" || ", paths)} | TRACE {trace}");
            }
        }
        Fail($"DIAG walks: {nFail}/40 failed: {results}");
        foreach (var f in fails)
            Fail("DIAG " + f);
    }

    private static int FlagsBetween(NavGraph nav, int a, int b)
    {
        for (var e = nav.EdgeStart[a]; e < nav.EdgeStart[a + 1]; e++)
            if (nav.EdgeTo[e] == b)
                return nav.EdgeFlags[e];
        return -2;
    }

    private static uint Hash(NavGraph g)
    {
        var h = 2166136261u;
        void Mix(uint v)
        {
            h ^= v;
            h *= 16777619u;
        }
        foreach (var n in g.Nodes)
        {
            Mix(BitConverter.SingleToUInt32Bits(n.X));
            Mix(BitConverter.SingleToUInt32Bits(n.Y));
            Mix(BitConverter.SingleToUInt32Bits(n.Z));
        }
        for (var e = 0; e < g.EdgeTo.Length; e++)
        {
            Mix((uint)g.EdgeTo[e]);
            Mix(g.EdgeFlags[e]);
            Mix(BitConverter.SingleToUInt32Bits(g.EdgeCost[e]));
        }
        foreach (var s in g.EdgeStart)
            Mix((uint)s);
        return h;
    }
}
