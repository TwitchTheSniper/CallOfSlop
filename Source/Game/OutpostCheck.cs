using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// <c>--outpost-check</c>, headless: a host on the story's planetoid (a throwaway save folder), the outposts of
/// Story/Outposts built, then the host's player taken to each outpost in turn and back to the colony. Prints where each
/// outpost went, its buildings, who got a body, what they said, a clan blood feud (they should fight), and what the
/// outposts cost: the look when nobody is near, and the server's update with an outpost's people awake against none.
/// Not a self-test: a hand tool for whoever changes the outposts or their data (docs/story/OUTPOSTS.md).
/// </summary>
public static class OutpostCheck
{
    public static int Run(string[] args)
    {
        var problems = new List<string>();
        void Say(string s) => Console.WriteLine($"[outpost-check] {s}");
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-outpost-check-{Environment.ProcessId}"), "outpost check");
        StarSystems.Reload();
        foreach (var d in OutpostData.All)
            if (d.Problem.Length > 0)
                problems.Add(d.Problem);
        if (Maps.Find(Game.StoryHomeMap) is not { } home || OutpostData.For(Game.StoryHomeMap) is not { } data)
        {
            Say($"no {Game.StoryHomeMap} map, or no outposts for it");
            return 1;
        }
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(26594, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: home.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11,
        };
        server.WorldAttach(Game.WorldNew("Outpost Check", home, survival: true));
        server.Mode = GameMode.Sandbox;
        double last = 0;
        double Run(double seconds, Func<bool>? until = null, List<double>? times = null)
        {
            var end = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < end)
            {
                var t = clock.Elapsed.TotalSeconds;
                var dt = Math.Min(t - last, 0.1);
                last = t;
                var sw = Stopwatch.StartNew();
                server.Update(t, dt);
                times?.Add(sw.Elapsed.TotalMilliseconds);
                if (until != null && until())
                    break;
                Thread.Sleep(1);
            }
            return clock.Elapsed.TotalSeconds;
        }
        Run(120, () => server.OutpostCount > 0 && server.OutpostsToBuild == 0 && clock.Elapsed.TotalSeconds > 20);
        World.Enter(server.Place);
        if (server.OutpostCount != data.Sites.Length)
            problems.Add($"{server.OutpostCount} outposts set up of {data.Sites.Length}");
        Say($"{server.OutpostCount} outposts, {server.OutpostBuilt} buildings built in {clock.Elapsed.TotalSeconds:0} s");
        if (server.OutpostBuilt < data.Sites.Sum(s => s.Buildings.Length))
            problems.Add($"only {server.OutpostBuilt} of {data.Sites.Sum(s => s.Buildings.Length)} buildings were built");
        if (server.Local is not { } me)
        {
            Say("no host player");
            return 1;
        }
        if (!me.Alive)
            server.SelfTestRespawn(me, clock.Elapsed.TotalSeconds);
        Run(1);
        me.ImmuneUntil = 1e9; // (the clan's blood feud shoots back)
        var colony = me.Position;

        // The look when nobody is near (the usual case), and the server's update with nobody awake.
        var now = clock.Elapsed.TotalSeconds;
        for (var i = 0; i < 2000; i++)
            server.OutpostTickForCheck(now);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int n = 20000;
        for (var i = 0; i < n; i++)
            server.OutpostTickForCheck(now);
        sw.Stop();
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Say($"the look with nobody near: {sw.Elapsed.TotalMilliseconds * 1000 / n:0.00} us a call, {bytes / (double)n:0.0} B a call (4 calls a second)");
        if (bytes > n)
            problems.Add($"the far look allocates {bytes / (double)n:0.0} B a call");
        var quiet = new List<double>();
        Run(6, times: quiet);

        for (var k = 0; k < server.OutpostCount; k++)
        {
            var st = data.Sites[k];
            if (!server.OutpostPlaced(k))
            {
                problems.Add($"{st.Name} wasn't placed");
                continue;
            }
            var mid = server.OutpostMiddle(k);
            var up = StandInPath.Up(mid);
            var side = StandInPath.Along(Vector3.Cross(up, Vector3.UnitX), mid);
            var stand = StandInPath.Ground(mid + side * 22f + up * 40f) + up * 0.1f;
            me.Teleport(stand);
            var said = server.OutpostLinesSaid;
            Run(10);
            // Up to one of them, to be spoken to (a greeting reaches 12 m).
            foreach (var o in server.Players.Values)
                if (o.Alive && server.OutpostPerson(o.Id) && Vector3.Distance(o.Position, mid) < 15f)
                {
                    stand = StandInPath.Ground(o.Position + side * 6f + up * 40f) + up * 0.1f;
                    break;
                }
            me.Teleport(stand);
            var busy = new List<double>();
            Run(5, times: busy);
            var lines = new List<string>();
            server.OutpostDescribe(lines);
            Say($"  (the player: alive {me.Alive}, bot {me.Bot != null}, {Vector3.Distance(me.Position, mid):0} m from the middle, {Vector3.Distance(me.Position, stand):0.0} m from where they were put)");
            Say($"{st.Name}: awake {server.OutpostAwake(k)}, {server.OutpostBodyCount} bodies, {server.OutpostLinesSaid - said} lines; the server's update {busy.Sum() / 5:0.00} ms a second, against {quiet.Sum() / 6:0.00} ms with nobody awake");
            foreach (var l in lines)
                Say("  " + l);
            if (server.OutpostBodyCount != st.People.Length)
                problems.Add($"{st.Name}: {server.OutpostBodyCount} bodies for {st.People.Length} people");
            if (server.OutpostLinesSaid == said)
                problems.Add($"{st.Name}: nobody said anything to a player 22 m off");
            if (st.Faction == "Clan MacSlop")
            {
                // A blood feud: the clan's people should come for the player.
                server.OutpostStandingForCheck(me, st.Faction, -90);
                me.Teleport(StandInPath.Ground(mid + side * 14f + up * 40f) + up * 0.1f);
                Run(4);
                lines.Clear();
                server.OutpostDescribe(lines);
                var fights = lines.Count(l => l.Contains("fighting", StringComparison.Ordinal));
                Say($"{st.Name} with a blood feud: {fights} of {lines.Count} fighting");
                if (fights == 0)
                    problems.Add($"{st.Name}: nobody fought a player with a blood feud");
                server.OutpostStandingForCheck(me, st.Faction, 0);
                me.Health = 100;
            }
            me.Teleport(colony);
            Run(8, () => server.OutpostBodyCount == 0 && !server.OutpostAwake(k));
            Run(1);
            if (server.OutpostBodyCount != 0 || server.OutpostAwake(k))
                problems.Add($"{st.Name}: its people kept their bodies after the player left");
        }
        Say($"bodies made {server.OutpostBodiesMade}, lines said {server.OutpostLinesSaid}");
        foreach (var p in problems)
            Say($"PROBLEM: {p}");
        Say(problems.Count == 0 ? "ok" : $"{problems.Count} problems");
        return problems.Count;
    }

    private static double Median(List<double> xs)
    {
        if (xs.Count == 0)
            return 0;
        xs.Sort();
        return xs[xs.Count / 2];
    }
}
