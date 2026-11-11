using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// TEMPORARY PROBE (feat/large-link-2): removed before the final push.
public static partial class SelfTest
{
    public static int ProbeConveyor(string[] args)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find(ConstructYardMap) is not { } yard)
            return 1;
        const ushort port = 26399;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        var folder = WorldStore.FolderFor("Conveyor probe 2");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Conveyor probe 2", yard, false);
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
        var splitDef = ConstructParts.Need("Conveyor Splitter");
        var rackDef = ConstructParts.Need("Piece Rack");
        Vector3 PortAt(ConstructPartDef d, string name) => d.NamedPorts.First(x => x.Type == RouteType.Conveyor && x.Name == name).At;
        byte PortByte(ConstructPartDef d, string name) => d.NamedPorts.First(x => x.Type == RouteType.Conveyor && x.Name == name).Byte;
        int Put(ConstructPartDef d, Vector3 portTarget, string portName)
        {
            var i = w.AddPart(line, d, portTarget - PortAt(d, portName), 0);
            w.AddLink(line, fnd, 0, i, 0); // (the probe holds it on: no building rules)
            return i;
        }
        // The splitter 3 m on from the die, a rack 2 m past its +z out, another 3 m out its +x out and 2 m on.
        var sp = Put(splitDef, p0 + new Vector3(0, 0, 3), "Piece in");
        var outZ = ConstructRoutes.PortPoint(line, sp, PortByte(splitDef, "Piece out"), RouteType.Conveyor);
        var outX = ConstructRoutes.PortPoint(line, sp, PortByte(splitDef, "Piece out 2"), RouteType.Conveyor);
        var r1 = Put(rackDef, outZ + new Vector3(0, 0, 2), "Piece in");
        var r2 = Put(rackDef, outX + new Vector3(3, 0, 2), "Piece in");
        line.Rebuild();
        Console.WriteLine($"[probe] parts: extruder {ei}, splitter {sp} (junction {splitDef.IsConveyorJunction}), racks {r1}, {r2}; out +z port {PortByte(splitDef, "Piece out")}, +x {PortByte(splitDef, "Piece out 2")}");
        CRouteNode N(Vector3 p) => new() { Pos = ConstructWorld.Quantise(p), Host = (ushort)fnd };
        var kIn = ConstructRoutes.Add(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, [], sp, PortByte(splitDef, "Piece in"));
        var kA = ConstructRoutes.Add(w, line, RouteType.Conveyor, sp, PortByte(splitDef, "Piece out"), 255, 0, default, [], r1, 255);
        var kB = ConstructRoutes.Add(w, line, RouteType.Conveyor, sp, PortByte(splitDef, "Piece out 2"), 255, 0, default, [N(outX + new Vector3(3, 0, 0))], r2, 255);
        line.Rebuild();
        Console.WriteLine($"[probe] conveyors {kIn} ({ConstructConveyor.Length(line, kIn):0.00} m, {line.Routes[kIn].ConvW}x{line.Routes[kIn].ConvH}), {kA} ({ConstructConveyor.Length(line, kA):0.00} m), {kB} ({ConstructConveyor.Length(line, kB):0.00} m); lead-in {ConstructConveyor.LeadIn(line, kA, kIn, out _, out _, out _):0.00} / {ConstructConveyor.LeadIn(line, kB, kIn, out _, out _, out _):0.00} m");
        var st = w.StoreOf(line, ei)!;
        var def = line.DefOf(ei);
        var metal = RecyclingTable.Current.IdOf("metal");
        void Cut(float m) => line.Parts[PartNamed(line, "Extruder")].Cfg[def.CfgSlot[(int)CfgKey.CutLength]] = m;
        void Feed()
        {
            var e = PartNamed(line, "Extruder");
            if (w.StoreOf(line, e) is { Amount: < 200 })
                w.Deposit(line, e, 200, metal);
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
        int RackN(Game g, int which)
        {
            if (g.Constructs.Get(line.Id) is not { } cc)
                return -1;
            var n = 0;
            for (var i = 0; i < cc.PartCount; i++)
                if (cc.DefOf(i).IsPieceRack && n++ == which)
                    return g.Constructs.StoreOf(cc, i)?.ItemCount ?? -1;
            return -1;
        }

        // 1. Extruder -> splitter -> two racks: 1 m pieces, round robin.
        Cut(1);
        var maxJump = 0f;
        var cl = a.Constructs.Get(line.Id);
        var ok1 = Until(() => w.StoreOf(line, r1)!.ItemCount + w.StoreOf(line, r2)!.ItemCount >= 6, 240);
        Cut(0);
        Until(() => line.ConveyedCount == 0, 60);
        Pump(server, [a], 2, ref now, clock);
        Console.WriteLine($"[probe] split: {(ok1 ? "ok" : "TIMED OUT")}; host racks {RackN(server, 0)} + {RackN(server, 1)}, client racks {RackN(a, 0)} + {RackN(a, 1)}; stall {(ExtrudeStall)st.Refused}");

        // 1b. Smoothness on the client: a piece's drawn front watched every 1/60 s across the junction.
        Cut(1);
        Until(() => line.ConveyedCount >= 1, 60);
        Cut(0);
        cl = a.Constructs.Get(line.Id);
        var prevPts = new List<Vector3>();
        var pts = new List<Vector3>();
        var steps = 0;
        var back = 0;
        var lastDesc = "";
        for (var t = 0; t < 60 * 14 && cl != null; t++)
        {
            Feed();
            Pump(server, [a], 1 / 60.0, ref now, clock);
            if (cl.ConveyedCount == 0)
                break;
            pts.Clear();
            var elapsed = (float)Math.Clamp(ConstructConveyor.Clock - cl.ConveyedStamp, 0, 0.25);
            for (var q = 0; q < cl.ConveyedCount; q++)
            {
                ref var r = ref cl.Conveyed[q];
                var d = ConstructParts.Def(r.Def);
                ConstructConveyor.Size(d, out _, out _, out var along);
                var len = ConstructConveyor.Length(cl, r.Route);
                var front = MathF.Max(r.At, MathF.Min(r.At + ConstructConveyor.Speed * elapsed, len)); // (as the drawing moves it on)
                ConstructConveyor.Pose(cl, r.Route, front - along * 0.5f, d, out var o, out var rq, out var seg, r.ViaPlus1 - 1);
                pts.Add(o + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, rq) + new Vector3(0, seg * 1000, 0)); // (its middle)
            }
            if (prevPts.Count == pts.Count)
            {
                foreach (var p in pts)
                {
                    var best = prevPts.Min(x => Vector3.Distance(x, p));
                    if (best > 0.05f)
                        Console.WriteLine($"[probe] jump {best * 100:0} cm at frame {t}: {string.Join(" | ", Enumerable.Range(0, cl.ConveyedCount).Select(q => $"r{cl.Conveyed[q].Route} at {cl.Conveyed[q].At:0.00} via {cl.Conveyed[q].ViaPlus1}"))}; now {string.Join(" ", pts)} was {string.Join(" ", prevPts)}; last {lastDesc}");
                    maxJump = MathF.Max(maxJump, best);
                }
                steps++;
            }
            (prevPts, pts) = (pts, prevPts);
            lastDesc = string.Join(" | ", Enumerable.Range(0, cl.ConveyedCount).Select(q => $"r{cl.Conveyed[q].Route} at {cl.Conveyed[q].At:0.00} via {cl.Conveyed[q].ViaPlus1}"));
        }
        Console.WriteLine($"[probe] client rider over {steps} frames: largest move of a middle in one frame {maxJump * 100:0.0} cm (0.5 m/s is 0.8 cm a frame); back-steps {back}");

        // 2. Blueprint round trip: sizes set, written as a blueprint, read and built again.
        line.Routes[kA].ConvW = 80;
        line.Routes[kA].ConvH = 60;
        line.Routes[kB].ConvW = 50;
        line.Routes[kB].ConvH = 45;
        var clip = ConstructClip.Of(line, "probe");
        var json = clip.ToBlueprint("Conveyor probe", "", out var bpWhy);
        if (json == null)
            Console.WriteLine($"[probe] blueprint: not written: {bpWhy}");
        else
        {
            var bp = ConstructBuild.ParseBlueprint(json, "probe");
            var again = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X + 30, spot.Z), 0, out var buildWhy);
            var sizes = again == null ? buildWhy : string.Join(", ", Enumerable.Range(0, again.RouteCount).Where(q => again.Routes[q].Type == RouteType.Conveyor).Select(q => $"{again.Routes[q].ConvW}x{again.Routes[q].ConvH}"));
            var want = string.Join(", ", Enumerable.Range(0, line.RouteCount).Where(q => line.Routes[q].Type == RouteType.Conveyor).Select(q => $"{line.Routes[q].ConvW}x{line.Routes[q].ConvH}"));
            Console.WriteLine($"[probe] blueprint: {json.Length} chars, \"size\" {json.Split("\"size\"").Length - 1} times; built back: {sizes}; source: {want}");
            var copy = ConstructClip.Of(line, "copy").Body;
            Console.WriteLine($"[probe] copy: {string.Join(", ", Enumerable.Range(0, copy.RouteCount).Where(q => copy.Routes[q].Type == RouteType.Conveyor).Select(q => $"{copy.Routes[q].ConvW}x{copy.Routes[q].ConvH}"))}");
            if (again != null)
                w.Remove(again);
        }
        line.Routes[kA].ConvW = line.Routes[kA].ConvH = line.Routes[kB].ConvW = line.Routes[kB].ConvH = 100;

        // 3. Rack drawing and F: the layout and the piece aimed at.
        var rs1 = w.StoreOf(line, r1)!;
        Span<ConstructConveyor.RackSlot> slots = stackalloc ConstructConveyor.RackSlot[ConstructConveyor.RackDrawn];
        var laid = ConstructConveyor.RackLayout(rackDef, rs1, slots);
        Console.WriteLine($"[probe] rack 1 holds {rs1.ItemCount} ({rs1.KindN} kinds), lays out {laid}: first at {(laid > 0 ? slots[0].Origin : default)}; look: {ConstructConveyor.Look(rackDef, rs1, true)}");

        // 4. Cost: 5 splitters, 20 conveyors, 200 riders, the conveyor step over 600 ticks.
        var extra = new List<int>();
        for (var j = 0; j < 5; j++)
        {
            var at = p0 + new Vector3(8 + 4 * j, 0, 12);
            var sj = Put(splitDef, at, "Piece in");
            line.Rebuild();
            extra.Add(ConstructRoutes.Add(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, [N(p0 + new Vector3(8 + 4 * j, 0, 4 + 0.02f * j))], sj, PortByte(splitDef, "Piece in")));
            foreach (var name in new[] { "Piece out", "Piece out 2", "Piece out 3" })
            {
                var o = ConstructRoutes.PortPoint(line, sj, PortByte(splitDef, name), RouteType.Conveyor);
                extra.Add(ConstructRoutes.Add(w, line, RouteType.Conveyor, sj, PortByte(splitDef, name), 255, 0, default, [N(o + ConstructRoutes.PortDir(line, sj, PortByte(splitDef, name), RouteType.Conveyor) * 12)], r1, 255));
            }
        }
        line.Rebuild();
        var beam = Extrusion.Def(new ExtrusionKey(Extrusion.Presets[4].Mask, 25, 25, 100, metal, false))!;
        foreach (var r in extra)
        {
            var len = ConstructConveyor.Length(line, r);
            for (var q = 0; q < 10; q++)
            {
                line.ReserveConveyed(line.ConveyedCount + 1);
                line.Conveyed[line.ConveyedCount++] = new CRider { Def = beam.Id, Route = (byte)r, At = len - 0.5f - q * 1.15f, Health = 255 };
            }
        }
        w.RidersChanged(line);
        Console.WriteLine($"[probe] cost run: {line.ConveyedCount} riders on {extra.Count} conveyors");
        var stepTimes = new List<double>();
        var sw = new Stopwatch();
        long stepAlloc = 0;
        for (var t = 0; t < 600; t++)
        {
            w.Step(World.Query, World.TickDt);
            if (t % ConstructConveyor.Every == 0)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                w.ConveyorTicks = ConstructConveyor.Every - 1;
                ConstructConveyor.Step(w, World.Query);
                stepTimes.Add(sw.Elapsed.TotalMilliseconds);
                var got = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (t > 300)
                    stepAlloc += got;
                if (got > 0 && t > 300)
                    Console.WriteLine($"[probe] step at tick {t} allocated {got} bytes; events {w.Events.Count}");
            }
        }
        stepTimes.Sort();
        Console.WriteLine($"[probe] conveyor step: median {stepTimes[stepTimes.Count / 2]:0.0000} ms, max {stepTimes[^1]:0.0000} ms over {stepTimes.Count} steps; {stepAlloc} bytes allocated in the steps after warm-up; {line.ConveyedCount} riding, rack 1 {rs1.ItemCount}");
        // (the client's step: the same 200 riders on the client copy)
        Pump(server, [a], 1.5, ref now, clock);
        cl = a.Constructs.Get(line.Id);
        if (cl != null)
        {
            var c0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            for (var t = 0; t < 60; t++)
            {
                cl.ConveyedStamp -= 0.11;
                ConstructConveyor.ClientStep(a.Constructs);
            }
            Console.WriteLine($"[probe] client step with {cl.ConveyedCount} riders: {sw.Elapsed.TotalMilliseconds / 60:0.0000} ms each, {GC.GetAllocatedBytesForCurrentThread() - c0} bytes");
        }
        foreach (var r in extra.OrderByDescending(x => x))
            ConstructRoutes.Remove(w, line, r);
        line.Rebuild();
        Pump(server, [a], 1, ref now, clock);

        // 5. The construct deleted mid-ride: every rider drops loose, on both peers.
        Console.WriteLine($"[probe] before the delete: stall {(ExtrudeStall)st.Refused}, riding {line.ConveyedCount}");
        foreach (var (r, at) in new[] { (kIn, 2.5f), (kA, 1.5f), (kB, 4f), (kB, 2f) })
            line.Conveyed[line.ConveyedCount++] = new CRider { Def = beam.Id, Route = (byte)r, At = at, Health = 255 };
        w.RidersChanged(line);
        Pump(server, [a], 1, ref now, clock);
        var riding = line.ConveyedCount;
        int Loose(ConstructWorld cw) => cw.Bodies.Count(b => b.PartCount == 1 && b.DefOf(0).IsExtrusion);
        var host0 = Loose(w);
        var client0 = Loose(a.Constructs);
        var doomed = new bool[line.PartCount];
        Array.Fill(doomed, true);
        w.RemoveParts(line, doomed);
        Pump(server, [a], 2, ref now, clock);
        Console.WriteLine($"[probe] deleted with {riding} riding: host loose pieces {host0} -> {Loose(w)}, client {client0} -> {Loose(a.Constructs)}; line gone on client: {a.Constructs.Get(line.Id) == null}");
        Console.WriteLine($"[probe] {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
        a.Dispose();
        server.Dispose();
        WorldStore.Delete(folder);
        return 0;
    }
}
