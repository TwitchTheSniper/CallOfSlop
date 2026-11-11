using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// STAND-IN: <c>--traffic-check [seconds]</c>, headless: a host on the story's planetoid with one client joined over the
/// real transport, the ambient traffic (GameTraffic.cs) hurried along for a minute or so. Prints the stations, every leg
/// the client was told of, how far the haulers ran from the ground, and what the host's look costs. Not a self-test: a
/// hand tool for whoever changes the traffic or its data (Story/Traffic/Traffic.json).
/// </summary>
public static class TrafficCheck
{
    public static int Run(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--traffic-check", StringComparison.OrdinalIgnoreCase));
        var seconds = at >= 0 && at + 1 < args.Length && double.TryParse(args[at + 1], out var sec) ? Math.Clamp(sec, 10, 600) : 75;
        var problems = new List<string>();
        void Say(string s) => Console.WriteLine($"[traffic-check] {s}");
        SavePaths.UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-traffic-check-{Environment.ProcessId}"), "traffic check");
        StarSystems.Reload();
        if (TrafficData.Current.Problem.Length > 0)
        {
            Say(TrafficData.Current.Problem);
            return 1;
        }
        if (Maps.Find(Game.StoryHomeMap) is not { } home)
        {
            Say($"the {Game.StoryHomeMap} map is missing");
            return 1;
        }
        const ushort port = 26593;
        var clock = Stopwatch.StartNew();
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: home.Id)
        {
            MapVoteEnabled = false, LocalWorldKey = 11,
        };
        server.WorldAttach(Game.WorldNew("Traffic Check", home, survival: true)); // (before the mode: the mode's reset builds the survival world's colony)
        server.Mode = GameMode.Sandbox;
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Ann", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) { LocalWorldKey = 22 };
        var seen = new Dictionary<ushort, (StandInKind Kind, uint Start)>();
        var legs = new int[(int)StandInKind.Count];
        int overs = 0, poses = 0, most = 0;
        float offGround = 0, lowest = float.MaxValue;
        var hurried = false;
        var until = seconds;
        double nextLook = 0, last = 0;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Min(t - last, 0.1);
            last = t;
            server.Update(t, dt);
            client.Update(t, dt);
            if (!hurried && server.TrafficStationCount > 0)
            {
                hurried = true;
                World.Enter(server.Place);
                server.TrafficHurry(t);
                for (var i = 0; i < server.TrafficStationCount; i++)
                {
                    var p = server.TrafficStationSpot(i);
                    Say($"station {TrafficData.Current.Stations[i].Name}: {p.X:0},{p.Y:0},{p.Z:0}");
                }
                until = t + seconds;
            }
            if (t >= nextLook && client.StandInsOf(World.Map))
            {
                nextLook = t + 0.25;
                World.Enter(client.Place);
                most = Math.Max(most, client.StandInCount);
                var tick = client.MoverRenderTick;
                for (var i = 0; i < client.StandInCount; i++)
                {
                    ref var c = ref client.StandIns[i];
                    if (!seen.TryGetValue(c.Id, out var was) || was.Start != c.Start && c.Kind != StandInKind.Pad)
                    {
                        if (c.Kind != StandInKind.Pad || !seen.ContainsKey(c.Id))
                            legs[(int)c.Kind]++;
                        if (c.Over)
                            overs++;
                        if (c.Kind != StandInKind.Pad)
                            Say($"{t - (until - seconds),5:0.0} s: craft {c.Id} {c.Kind}{(c.Over ? " overhead" : c.Route ? " leg" : " visit")}: {(c.Route ? Vector3.Distance(c.From, c.Land) : 0):0} m in {c.In / (double)World.TickRate:0} s, stands {c.Wait / (double)World.TickRate:0} s, tint {c.Tint.X:0.00},{c.Tint.Y:0.00},{c.Tint.Z:0.00}");
                    }
                    seen[c.Id] = (c.Kind, c.Start);
                    if (!StandInPath.Pose(ref c, tick, out var frame, out _, out _))
                        continue;
                    poses++;
                    var pos = frame.Translation;
                    if (!float.IsFinite(pos.X + pos.Y + pos.Z + frame.M11 + frame.M22 + frame.M33))
                    {
                        problems.Add($"craft {c.Id} ({c.Kind}) posed at {pos}");
                        continue;
                    }
                    var ground = StandInPath.Ground(pos + StandInPath.Up(pos) * 60f);
                    var high = Vector3.Dot(pos - ground, StandInPath.Up(pos));
                    if (c.Kind == StandInKind.Hauler && c.Route)
                        offGround = MathF.Max(offGround, MathF.Abs(high - 0.15f));
                    else if (c.Kind != StandInKind.Pad)
                        lowest = MathF.Min(lowest, high);
                }
            }
            Thread.Sleep(2);
        }
        World.Enter(server.Place);
        Say($"host: {server.StandInCount} craft and pads now, {server.TrafficTrips} trips begun, {server.TrafficRadioLines} radio lines; client: {client.StandInCount} now, {most} at most, {poses} poses");
        Say($"client saw: {legs[(int)StandInKind.Pad]} pads, {legs[(int)StandInKind.Hauler]} hauler legs, {legs[(int)StandInKind.Courier]} courier, {legs[(int)StandInKind.Shuttle]} shuttle, {legs[(int)StandInKind.Freighter]} freighter ({overs} overhead)");
        Say($"haulers at most {offGround:0.00} m off their hover height; flyers no lower than {(lowest == float.MaxValue ? 0 : lowest):0.0} m over the ground");
        var lines = new List<string>();
        server.TrafficDescribe(lines);
        foreach (var l in lines)
            Say(l);
        if (!hurried)
            problems.Add("no traffic was set up (no colony?)");
        if (legs[(int)StandInKind.Pad] == 0 || legs[(int)StandInKind.Hauler] == 0)
            problems.Add("the client saw no pad or no hauler leg");
        if (offGround > 1.5f)
            problems.Add($"a hauler ran {offGround:0.0} m off the ground");
        if (lowest < -1f)
            problems.Add($"a flyer went {-lowest:0.0} m under the ground");

        // What the host's look costs when nothing is due (the usual case), four times a second.
        var now = clock.Elapsed.TotalSeconds;
        for (var i = 0; i < 2000; i++)
            server.TrafficTickForCheck(now);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int n = 20000;
        for (var i = 0; i < n; i++)
            server.TrafficTickForCheck(now);
        sw.Stop();
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Say($"the host's look: {sw.Elapsed.TotalMilliseconds * 1000 / n:0.00} us a call, {bytes / (double)n:0.0} B a call (4 calls a second)");
        if (bytes > n)
            problems.Add($"the look allocates {bytes / (double)n:0.0} B a call");
        foreach (var p in problems)
            Say($"PROBLEM: {p}");
        Say(problems.Count == 0 ? "ok" : $"{problems.Count} problems");
        return problems.Count;
    }
}
