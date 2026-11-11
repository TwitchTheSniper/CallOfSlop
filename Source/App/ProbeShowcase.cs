using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// TEMPORARY PROBE (feat/production-showcase): removed before the final push.
public static partial class SelfTest
{
    public static int ProbeShowcase(string[] args)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find("Proving Ground") is not { } map)
            return 1;
        const ushort port = 26431;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        var folder = WorldStore.FolderFor("Showcase probe");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Showcase probe", map, false);
        var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(session);
        var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        var worst = 0.0;
        var worstAt = 0.0;
        var timing = false;
        var ticks = new List<double>();
        void Run(double seconds)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until)
            {
                var t = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(t - now, 0.001, 0.1);
                now = t;
                var t0 = Stopwatch.GetTimestamp();
                server.Update(now, dt);
                var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (timing)
                {
                    ticks.Add(ms);
                    if (ms > worst)
                        (worst, worstAt) = (ms, now);
                }
                a.Update(now, dt);
                Thread.Sleep(2);
            }
        }
        Run(1.0);
        for (var wait = 0; wait < 80 && (server.Phase != MatchPhase.Playing || a.LocalId == byte.MaxValue); wait++)
            Run(0.25);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var player))
        {
            Console.WriteLine("[probe] no join");
            return 1;
        }
        if (!player.Alive)
            server.SelfTestRespawn(player, now);
        Run(0.5);
        var w = server.Constructs;
        var index = ConstructBuild.Blueprints.ToList().FindIndex(b => b.Name == "Production Showcase");
        Console.WriteLine($"[probe] creative {server.SandboxInfiniteParts}; blueprint {index}; spawn: {server.SandboxSpawnBlueprint(player, index) ?? "asked"}");
        Construct? c = null;
        for (var k = 0; k < 200 && c == null; k++)
        {
            Run(0.25);
            c = w.Bodies.FirstOrDefault(b => b.PartCount > 200 && Enumerable.Range(0, b.PartCount).Any(i => b.DefOf(i).Name == "Line Console"));
        }
        if (c == null)
        {
            Console.WriteLine("[probe] it never came down");
            return 1;
        }
        Console.WriteLine($"[probe] down as construct {c.Id}: {c.PartCount} parts, {c.RouteCount} links, anchored {c.Anchored}, charge {c.Charge:0}/{c.ChargeCapacity:0} kJ");
        int Nth(string name, int n = 0) => Enumerable.Range(0, c!.PartCount).Where(i => c.DefOf(i).Name == name).Skip(n).First();
        var refine = Nth("Line Console", 0);
        var orders = Nth("Line Console", 1);
        Span<int> list = stackalloc int[32];
        Console.WriteLine($"[probe] refining console runs {ProductionLines.Machines(c, refine, list)}; orders console runs {ProductionLines.Machines(c, orders, list)}; bill lines {c.Parts[orders].Orders?.Count}");
        var machines = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i) is { } d && (d.IsRefiner || d.IsExtruder || d.IsPress)).ToArray();
        var stock = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).IsVolumeStore).ToArray();
        Console.WriteLine($"[probe] stock at spawn: {string.Join(", ", stock.Select(i => w.StoreOf(c, i) is { } st ? $"{RecyclingTable.Current.NameOf(st.Material)} {st.Amount}" : "none"))}");
        Run(4.0); // (the chutes fill the machines)
        foreach (var i in machines)
            if (w.StoreOf(c, i) is { } st0)
                Console.WriteLine($"[probe] at 4 s {c.DefOf(i).Name}: in {RecyclingTable.Current.NameOf(st0.InMaterial)} {st0.InAmount}, out {st0.Amount}, refused {st0.Refused}");
        Console.WriteLine($"[probe] after 4 s: {string.Join(", ", machines.Select(i => $"{c.DefOf(i).Name} {(w.StoreOf(c, i) is { } st ? RecyclingTable.Current.NameOf(st.Material) + " " + st.Amount : "-")} {ProductionLines.Raw(w, c, i, w.StoreOf(c, i)!)}"))}");
        // The sign's steps: Start on the refining console; Bill, Send, Line, Start on the orders console.
        c.Parts[refine].State = 1;
        Console.WriteLine($"[probe] send: {WorkOrders.BillSend(w, c, orders) ?? "ok"}");
        c.Parts[orders].State = 1;
        foreach (var m in machines)
            if (c.Parts[m].Orders is { } q)
                Console.WriteLine($"[probe]   {c.DefOf(m).Name} #{m}: {q.Count} orders, {q.PiecesLeft} pieces");
        var seen = machines.ToDictionary(i => i, _ => new HashSet<LineState>());
        var pressTook = false;
        var spurWhy = "";
        timing = true;
        Run(3.0);
        Console.WriteLine($"[probe] timing from {now:0.0} s (worst before: {worst:0.00} ms at {worstAt:0.0} s)");
        worst = 0;
        ticks.Clear();
        for (var s = 0; s < 100; s++)
        {
            Run(1.0);
            foreach (var m in machines)
                if (w.StoreOf(c, m) is { } st && ProductionLines.Raw(w, c, m, st) is { } ls)
                    seen[m].Add(ls);
            var pr = Nth("Press");
            pressTook |= ConstructPress.PieceIn(c, pr, 0) >= 0 || ConstructPress.PieceIn(c, pr, 1) >= 0;
            var spur = machines.Where(i => c.DefOf(i).Name == "Extruder").Skip(2).First();
            if ((ExtrudeStall)w.StoreOf(c, spur)!.Refused == ExtrudeStall.ConveyorSmall)
                spurWhy = ConstructExtruder.Status(c, spur, w.StoreOf(c, spur)!);
            if (s % 30 == 29)
                Console.WriteLine($"[probe] t+{s + 1} s: racks {Racks(server)}; queues {string.Join(" / ", machines.Where(i => c.Parts[i].Orders != null).Select(i => c.Parts[i].Orders!.PiecesLeft))}; loose {w.Bodies.Count(b => b.PartCount == 1)}");
        }
        timing = false;
        Run(2.0);
        string Racks(Game g)
        {
            if (g.Constructs.Get(c!.Id) is not { } cc)
                return "no body";
            var parts = new List<string>();
            for (var i = 0; i < cc.PartCount; i++)
                if (cc.DefOf(i).IsPieceRack && g.Constructs.StoreOf(cc, i) is { } st)
                    parts.Add($"{cc.DefOf(i).Name}#{i}[{string.Join(", ", Enumerable.Range(0, st.KindN).Select(k => $"{Extrusion.Name(ConstructParts.Def(st.KindDef![k]).ExKey)} x{st.KindCount![k]}"))}]");
            return string.Join(" ", parts);
        }
        Console.WriteLine($"[probe] host racks:   {Racks(server)}");
        Console.WriteLine($"[probe] client racks: {Racks(a)}");
        Console.WriteLine($"[probe] same on both: {Racks(server) == Racks(a)}");
        foreach (var m in machines)
            Console.WriteLine($"[probe] {c.DefOf(m).Name} #{m}: states {string.Join(",", seen[m])}; now {ProductionLines.Raw(w, c, m, w.StoreOf(c, m)!)} {(c.DefOf(m).IsExtruder ? ConstructExtruder.Status(c, m, w.StoreOf(c, m)!) : "")}");
        Console.WriteLine($"[probe] press took a piece: {pressTook}; loose bodies {string.Join(", ", w.Bodies.Where(b => b.PartCount == 1).GroupBy(b => b.DefOf(0).Name).Select(gr => $"{gr.Key} x{gr.Count()}"))}");
        for (var q = 0; q < c.ConveyedCount; q++)
            Console.WriteLine($"[probe] rider {q}: route {c.Conveyed[q].Route} ({c.DefOf(c.Routes[c.Conveyed[q].Route].From).Name} -> {c.DefOf(c.Routes[c.Conveyed[q].Route].To).Name}, {ConstructConveyor.Length(c, c.Conveyed[q].Route):0.00} m) at {c.Conveyed[q].At:0.00} via {c.Conveyed[q].ViaPlus1}: {ConstructParts.Def(c.Conveyed[q].Def).Name}");
        foreach (var i in stock.Concat(machines))
            if (w.StoreOf(c, i) is { } st)
                Console.WriteLine($"[probe] store {c.DefOf(i).Name} #{i}: in {RecyclingTable.Current.NameOf(st.InMaterial)} {st.InAmount}, out {RecyclingTable.Current.NameOf(st.Material)} {st.Amount}, refused {st.Refused}, blocked {st.Blocked}");
        Console.WriteLine($"[probe] press look: {ConstructPress.Look(c, Nth("Press"), w.StoreOf(c, Nth("Press"))!, false, null, Vector3.Zero)}");
        Console.WriteLine($"[probe] spur refusal: \"{spurWhy}\"");
        Console.WriteLine($"[probe] bill left: {c.Parts[orders].Orders?.Count ?? 0} lines, note \"{c.Parts[orders].Orders?.Note}\"");
        ticks.Sort();
        Console.WriteLine($"[probe] server tick: worst {worst:0.00} ms at {worstAt:0} s, p99 {ticks[(int)(ticks.Count * 0.99)]:0.00} ms, median {ticks[ticks.Count / 2]:0.00} ms over {ticks.Count} updates; charge {c.Charge:0} kJ, plant {c.PlantOut:0.0} kW, use {c.PowerUse:0} W");
        Console.WriteLine($"[probe] {NetSafety.Dropped} packets dropped");
        a.Dispose();
        server.Dispose();
        WorldStore.Delete(folder);
        return 0;
    }
}
