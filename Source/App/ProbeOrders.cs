using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// TEMPORARY PROBE (feat/work-orders-1): removed before the final push.
public static partial class SelfTest
{
    public static int ProbeOrders(string[] args)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find(ConstructYardMap) is not { } yard)
            return 1;
        const ushort port = 26411;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        var folder = WorldStore.FolderFor("Orders probe");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Orders probe", yard, false);
        var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(session);
        var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Console.WriteLine($"[probe] client network: {a.Net.SimulateBadNetwork(80, 16, 2)}");
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 80 && (server.Phase != MatchPhase.Playing || a.LocalId == byte.MaxValue); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        if (a.LocalId == byte.MaxValue)
        {
            Console.WriteLine("[probe] no join");
            return 1;
        }
        var w = server.Constructs;
        var spot = OpenSpot(w) ?? new Vector3(0, 0, 0);
        var line = BuildBlueprint(w, "Extruder Line", spot.X, spot.Z);
        if (line == null)
            return 1;
        line.Owner = 255;
        ConstructRoutes.RemoveAt(w, line, PartNamed(line, "Timer"), RouteType.Data);
        var fnd = PartNamed(line, "Foundation");
        var ei = PartNamed(line, "Extruder");
        var p0 = ConstructRoutes.PortPoint(line, ei, 255, RouteType.Conveyor);
        var rackDef = ConstructParts.Need("Piece Rack");
        var consoleDef = ConstructParts.Need("Line Console");
        var exDef = ConstructParts.Need("Extruder");
        Vector3 PortAt(ConstructPartDef d, string name) => d.NamedPorts.First(x => x.Type == RouteType.Conveyor && x.Name == name).At;
        int Put(ConstructPartDef d, Vector3 at)
        {
            var i = w.AddPart(line, d, at, 0);
            w.AddLink(line, fnd, 0, i, 0); // (the probe holds it on: no building rules)
            return i;
        }
        var rack = Put(rackDef, p0 + new Vector3(0, 0, 3) - PortAt(rackDef, "Piece in"));
        var con = Put(consoleDef, p0 + new Vector3(-3, -0.5f, -2));
        line.Rebuild();
        ConstructRoutes.Add(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, [], rack, 255);
        ConstructRoutes.Add(w, line, RouteType.Data, con, 255, 255, 0, default, [], ei, 255);
        line.Rebuild();
        var metal = RecyclingTable.Current.IdOf("metal");
        var wood = RecyclingTable.Current.IdOf("wood");
        int Ex() => PartNamed(line, "Extruder");
        int Con() => PartNamed(line, "Line Console");
        int Rack() => PartNamed(line, "Piece Rack");
        void Feed()
        {
            for (var i = 0; i < line.PartCount; i++)
                if (line.DefOf(i).IsExtruder && w.StoreOf(line, i) is { Amount: < 200 })
                    w.Deposit(line, i, 200, metal);
            line.Charge = line.ChargeCapacity;
        }
        bool Until(Func<bool> ok, double seconds)
        {
            for (var t = 0.0; t < seconds && !ok(); t += 0.25)
            {
                Feed();
                Pump(server, [a], 0.25, ref now, clock);
            }
            return ok();
        }
        string Counts(Game g)
        {
            if (g.Constructs.Get(line.Id) is not { } cc)
                return "no line";
            for (var i = 0; i < cc.PartCount; i++)
                if (cc.DefOf(i).IsPieceRack && g.Constructs.StoreOf(cc, i) is { } st)
                    return string.Join(", ", Enumerable.Range(0, st.KindN).Select(k => $"{Extrusion.Name(ConstructParts.Def(st.KindDef![k]).ExKey)} x{st.KindCount![k]}"));
            return "no rack";
        }
        WorkOrder O(int preset, int wcm, int hcm, int lcm, int n, byte mat, ExShape shape = ExShape.Section) =>
            new() { Key = new ExtrusionKey(shape == ExShape.Section ? Extrusion.Presets[preset].Mask : 0, (ushort)wcm, (ushort)hcm, (ushort)lcm, mat, false, shape), Part = ConstructParts.None, Count = (ushort)n };
        Feed();
        Pump(server, [a], 1, ref now, clock);

        // 1. A bill of seven beam sizes in metal, sent to one Extruder: all seven counts in order, on the conveyor to the rack.
        var bill = new[]
        {
            O(4, 25, 25, 50, 2, metal), O(4, 25, 25, 100, 2, metal), O(4, 40, 40, 75, 1, metal), O(0, 10, 10, 50, 3, metal),
            O(3, 30, 30, 60, 2, metal), O(5, 20, 30, 80, 1, metal), O(0, 20, 20, 100, 2, metal, ExShape.Round),
        };
        foreach (var o in bill)
            Console.WriteLine($"[probe] add: {WorkOrders.AddToBill(w, line, Con(), o) ?? "ok"}");
        line.Parts[Con()].State = 1; // (Start)
        Console.WriteLine($"[probe] send: {WorkOrders.BillSend(w, line, Con()) ?? "ok"}; queue {line.Parts[Ex()].Orders?.Count}, pieces {line.Parts[Ex()].Orders?.PiecesLeft}");
        Pump(server, [a], 1, ref now, clock);
        var cl = a.Constructs.Get(line.Id);
        Console.WriteLine($"[probe] client queue: total {cl?.Parts[Ex()].Orders?.Total}, head shown {cl?.Parts[Ex()].Orders?.Count}, pieces {cl?.Parts[Ex()].Orders?.PiecesLeft}; lamp {ProductionLines.Raw(a.Constructs, cl!, Ex(), a.Constructs.StoreOf(cl!, Ex())!)}");
        var order = new List<string>();
        var lastHead = "";
        var ok1 = Until(() =>
        {
            if (line.Parts[Ex()].Orders is { Count: > 0 } q && Extrusion.Name(q.Head.Key) is var h && h != lastHead)
                order.Add(lastHead = h);
            return (w.StoreOf(line, Rack())?.ItemCount ?? 0) >= 13;
        }, 400);
        Pump(server, [a], 2, ref now, clock);
        Console.WriteLine($"[probe] seven sizes: {(ok1 ? "ok" : "TIMED OUT")} after {now:0} s; heads in turn: {string.Join(" | ", order)}");
        Console.WriteLine($"[probe] host rack:   {Counts(server)}");
        Console.WriteLine($"[probe] client rack: {Counts(a)}");
        Console.WriteLine($"[probe] same on both: {Counts(server) == Counts(a)}; queue left {line.Parts[Ex()].Orders?.Count}, stall {(ExtrudeStall)w.StoreOf(line, Ex())!.Refused}, client lamp {ProductionLines.Raw(a.Constructs, a.Constructs.Get(line.Id)!, Ex(), a.Constructs.StoreOf(a.Constructs.Get(line.Id)!, Ex())!)}");

        // 2. An order in a material the machine isn't fed: refused, the reason on the client's console screen.
        WorkOrders.AddToBill(w, line, Con(), O(4, 25, 25, 50, 1, wood));
        Console.WriteLine($"[probe] wood order: {WorkOrders.BillSend(w, line, Con())}");
        Console.WriteLine($"[probe] machine screen refusal at the Queue button with a Custom-free bad size: {WorkOrders.Queue(w, line, Ex(), O(4, 90, 90, 50, 1, metal))}");
        Pump(server, [a], 1.5, ref now, clock);
        cl = a.Constructs.Get(line.Id);
        Console.WriteLine($"[probe] client bill note: \"{cl?.Parts[Con()].Orders?.Note}\" ({cl?.Parts[Con()].Orders?.Total} line kept)");
        WorkOrders.BillClear(w, line, Con());

        // 3. Two Extruders on one console share a bill.
        var ex2 = Put(exDef, line.Parts[Ex()].Pos + new Vector3(4, 0, 0));
        line.Rebuild();
        ConstructRoutes.Add(w, line, RouteType.Data, Con(), 255, 255, 0, default, [], ex2, 255);
        line.Rebuild();
        Feed();
        Pump(server, [a], 0.5, ref now, clock);
        foreach (var o in bill.Take(5))
            WorkOrders.AddToBill(w, line, Con(), o);
        Console.WriteLine($"[probe] share: {WorkOrders.BillSend(w, line, Con()) ?? "ok"}");
        string Q(Construct c, int i) => c.Parts[i].Orders is { } q ? $"{q.Total} orders: {string.Join(", ", q.Items.Take(q.Count).Select(o => Extrusion.Label(o.Key) + $" {o.Key.L}cm x{o.Count}"))}" : "none";
        var e1 = Ex();
        var e2 = Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef);
        Console.WriteLine($"[probe] extruder A {Q(line, e1)}");
        Console.WriteLine($"[probe] extruder B {Q(line, e2)}");
        var ok3 = Until(() => line.Parts[e1].Orders is { Count: 0 } && line.Parts[Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef)].Orders is { Count: 0 }, 300);
        Console.WriteLine($"[probe] shared bill worked off: {ok3} at {now:0} s; rack {w.StoreOf(line, Rack())?.ItemCount}; loose pieces {w.Bodies.Count(b => b.PartCount == 1 && b.DefOf(0).IsExtrusion)}");

        // 4. The bill from a shipped blueprint.
        if (ConstructBuild.FindBlueprint("Fabric Flyer") is { } bp)
        {
            var b = ConstructBill.FromBlueprint(bp);
            Console.WriteLine($"[probe] Fabric Flyer bill: {string.Join("; ", b.Pieces.Select(o => $"{o.Count} x {Extrusion.Name(o.Key)}"))}");
            Console.WriteLine($"[probe] onto the console: {ConstructBill.ToConsole(w, line, Con(), bp) ?? "ok"}; send: {WorkOrders.BillSend(w, line, Con())}");
            WorkOrders.BillClear(w, line, Con());
        }

        // 5. Save and load: a queue and a bill.
        WorkOrders.Queue(w, line, Ex(), O(4, 25, 25, 50, 5, metal));
        WorkOrders.AddToBill(w, line, Con(), O(0, 10, 10, 50, 3, metal));
        line.Parts[Con()].State = 0; // (stopped, so the queue stays)
        Pump(server, [a], 0.5, ref now, clock);
        var savedQ = Q(line, Ex());
        var savedB = Q(line, Con());
        Console.WriteLine($"[probe] save: {server.WorldSaveNow(true)}");

        // 6. Cost: a queue of 200 orders on the second Extruder, the Extruder step over 600 ticks; an idle machine.
        var e2n = Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef);
        line.Parts[Con()].State = 1;
        for (var k = 0; k < 200; k++)
            WorkOrders.Queue(w, line, e2n, O(k % 9, 20 + 5 * (k % 3), 20, 20 + 5 * (k % 7), 1, metal));
        Console.WriteLine($"[probe] cost run: queue {line.Parts[e2n].Orders?.Count}");
        var times = new List<double>();
        var sw = new Stopwatch();
        long alloc = 0;
        for (var t = 0; t < 600; t++)
        {
            Feed();
            w.ExtrudeTicks = int.MinValue / 2; // (only the probe steps it)
            w.Step(World.Query, World.TickDt);
            if (t % ConstructExtruder.Every == 0)
            {
                w.ExtrudeTicks = ConstructExtruder.Every - 1;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                ConstructExtruder.Step(w, World.Query);
                times.Add(sw.Elapsed.TotalMilliseconds);
                var got = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (t > 120)
                    alloc += got;
                if (got > 0 && t > 120)
                    Console.WriteLine($"[probe] step {t}: {got} bytes; queue {line.Parts[Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef)].Orders?.Count}, bodies {w.Bodies.Count}, defs {Extrusion.DefCount}");
            }
        }
        times.Sort();
        Console.WriteLine($"[probe] extruder step (2 machines, 200-order queue): median {times[times.Count / 2]:0.0000} ms, max {times[^1]:0.0000} ms over {times.Count} steps; {alloc} bytes after warm-up; queue now {line.Parts[Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef)].Orders?.Count}");
        // An idle line: both queues empty (one cleared, one emptied), the step over 300 ticks.
        line.Parts[Enumerable.Range(0, line.PartCount).Last(i => line.DefOf(i) == exDef)].Orders!.Clear();
        line.Parts[Ex()].Orders?.Clear();
        times.Clear();
        alloc = 0;
        for (var t = 0; t < 300; t++)
        {
            w.ExtrudeTicks = int.MinValue / 2;
            w.Step(World.Query, World.TickDt);
            if (t % ConstructExtruder.Every == 0)
            {
                w.ExtrudeTicks = ConstructExtruder.Every - 1;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                ConstructExtruder.Step(w, World.Query);
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (t > 60)
                    alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
        }
        times.Sort();
        Console.WriteLine($"[probe] idle (empty queues): median {times[times.Count / 2]:0.0000} ms, max {times[^1]:0.0000} ms; {alloc} bytes");
        w.ExtrudeTicks = 0;
        Pump(server, [a], 1, ref now, clock);
        Console.WriteLine($"[probe] {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
        a.Dispose();
        server.Dispose();

        var entry = WorldStore.List().FirstOrDefault(e => e.Folder == folder);
        if (entry != null && Game.WorldOpen(entry, out var why) is { } back)
        {
            using var g2 = ModeGame(0, GameMode.Sandbox, yard.Id);
            g2.WorldAttach(back);
            Pump(g2, [], 0.5, ref now, clock);
            var again = g2.Constructs.Bodies.FirstOrDefault(b => Enumerable.Range(0, b.PartCount).Any(i => b.DefOf(i) == consoleDef));
            if (again != null)
            {
                var ae = PartNamed(again, "Extruder");
                var ac = PartNamed(again, "Line Console");
                Console.WriteLine($"[probe] loaded queue: {Q(again, ae)} (saved {savedQ})");
                Console.WriteLine($"[probe] loaded bill:  {Q(again, ac)} (saved {savedB})");
            }
            else
                Console.WriteLine("[probe] loaded: no line");
        }
        else
            Console.WriteLine("[probe] the world didn't reopen");
        WorldStore.Delete(folder);
        return 0;
    }
}
