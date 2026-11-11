using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// TEMPORARY PROBE (feat/large-link-1): removed before the final push.
public static partial class SelfTest
{
    public static int ProbeConveyor(string[] args)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find(ConstructYardMap) is not { } yard)
            return 1;
        const ushort port = 26398;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        var folder = WorldStore.FolderFor("Conveyor probe");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Conveyor probe", yard, false);
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
        var rack = Fit(w, line, fnd, "top33", "Piece Rack", "-y.0.0");
        line.Rebuild();
        var ei = PartNamed(line, "Extruder");
        rack = PartNamed(line, "Piece Rack");
        Console.WriteLine($"[probe] line: {line.PartCount} parts, extruder {ei}, rack {rack}");
        // The conveyor: out of the die's Piece out, along +z 4 m, across to the rack's side, back into its Piece in (the probe skips the laying rule; it is printed).
        var p0 = ConstructRoutes.PortPoint(line, ei, 255, RouteType.Conveyor);
        var p1 = ConstructRoutes.PortPoint(line, rack, 255, RouteType.Conveyor);
        Console.WriteLine($"[probe] ports: out {p0}, in {p1}");
        CRouteNode[] nodes = [new() { Pos = ConstructWorld.Quantise(p0 + new Vector3(0, 0, 4)), Host = (ushort)fnd }, new() { Pos = ConstructWorld.Quantise(new Vector3(p1.X, p0.Y, p0.Z + 4)), Host = (ushort)fnd }];
        Console.WriteLine($"[probe] the laying rule on this route: {ConstructRoutes.Check(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, nodes, rack, 255) ?? "ok"}");
        var k = ConstructRoutes.Add(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, nodes, rack, 255);
        line.Rebuild();
        Console.WriteLine($"[probe] conveyor {k}: {line.Routes[k].ConvW} x {line.Routes[k].ConvH} cm, {ConstructConveyor.Length(line, k):0.00} m");
        var st = w.StoreOf(line, ei)!;
        var rs = w.StoreOf(line, rack)!;
        var def = line.DefOf(ei);
        var metal = RecyclingTable.Current.IdOf("metal");
        line.Charge = line.ChargeCapacity;
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
        string Kinds(CStore s)
        {
            var parts = new List<string>();
            for (var q = 0; q < s.KindN; q++)
                parts.Add($"{ConstructParts.Get(s.KindDef![q])?.ExKey.L}cm x{s.KindCount![q]}");
            return string.Join(", ", parts);
        }
        CStore? ClientRack() => a.Constructs.Get(line.Id) is { } cc && PartNamed(cc, "Piece Rack") is var cr and >= 0 ? a.Constructs.StoreOf(cc, cr) : null;

        // 1. Two lengths: three 2 m beams, then two 4 m ones.
        Cut(2);
        var ok1 = Until(() => rs.ItemCount >= 3, 120);
        Cut(4);
        var ok2 = Until(() => rs.ItemCount >= 5 && line.ConveyedCount == 0, 160);
        Cut(0);
        Pump(server, [a], 2, ref now, clock);
        var cs = ClientRack();
        Console.WriteLine($"[probe] two lengths: {(ok1 && ok2 ? "ok" : "TIMED OUT")}; host rack {rs.ItemCount}: {Kinds(rs)}; client rack {cs?.ItemCount}: {(cs != null ? Kinds(cs) : "none")}");
        var keysMatch = cs != null && cs.KindN == rs.KindN && Enumerable.Range(0, rs.KindN).All(q => ConstructParts.Get(cs.KindDef![q])!.ExKey == ConstructParts.Get(rs.KindDef![q])!.ExKey && cs.KindCount![q] == rs.KindCount![q]);
        Console.WriteLine($"[probe] client keys and counts match the host's: {keysMatch}");

        // 2. Riders seen on the client while they ride.
        Cut(2);
        Until(() => line.ConveyedCount >= 2, 60);
        Pump(server, [a], 1, ref now, clock);
        var cl = a.Constructs.Get(line.Id);
        Console.WriteLine($"[probe] riding: host {line.ConveyedCount}, client {cl?.ConveyedCount}; client keys match: {cl != null && cl.ConveyedCount == line.ConveyedCount && Enumerable.Range(0, line.ConveyedCount).All(q => ConstructParts.Def(cl.Conveyed[q].Def).ExKey == ConstructParts.Def(line.Conveyed[q].Def).ExKey)}");
        Console.WriteLine($"[probe] fronts host {string.Join(" ", line.Conveyed.Take(line.ConveyedCount).Select(r => r.At.ToString("0.00")))}, client {(cl == null ? "" : string.Join(" ", cl.Conveyed.Take(cl.ConveyedCount).Select(r => r.At.ToString("0.00"))))}");

        // 3. Cut the link mid-ride: every piece on it drops as a loose body.
        Cut(0);
        var riding = line.ConveyedCount;
        var bodies0 = w.Bodies.Count(b => b.PartCount == 1 && b.DefOf(0).IsExtrusion);
        ConstructRoutes.Damage(w, line, k, 1, 1e6f);
        Pump(server, [a], 1.5, ref now, clock);
        var dropped = w.Bodies.Count(b => b.PartCount == 1 && b.DefOf(0).IsExtrusion) - bodies0;
        var clientLoose = a.Constructs.Bodies.Count(b => b.PartCount == 1 && b.DefOf(0).IsExtrusion);
        Console.WriteLine($"[probe] cut mid-ride: {riding} riding, {dropped} dropped loose on the host, {clientLoose} loose pieces on the client, {line.ConveyedCount} left riding (client {a.Constructs.Get(line.Id)?.ConveyedCount})");
        ConstructRoutes.Repair(w, line, k);
        line.Rebuild();

        // 4. Too big: the conveyor set to 10 x 10 cm refuses a 25 cm section at the machine.
        line.Routes[k].ConvW = line.Routes[k].ConvH = 10;
        w.StructureDirty.Add(line.Id);
        Cut(1);
        Until(() => (ExtrudeStall)st.Refused == ExtrudeStall.ConveyorSmall, 30);
        Pump(server, [a], 1, ref now, clock);
        var cline = a.Constructs.Get(line.Id);
        var cst = cline != null ? a.Constructs.StoreOf(cline, PartNamed(cline, "Extruder")) : null;
        Console.WriteLine($"[probe] too big: host stall {(ExtrudeStall)st.Refused}; client look: {(cline != null && cst != null ? ConstructExtruder.Look(cline, PartNamed(cline, "Extruder"), cst, false, new SandboxInventory()) : "none")}");
        Console.WriteLine($"[probe] size check: 120 x 50 -> {ConstructConveyor.SizeRefusal(line, k, 120, 50) ?? "ok"}; 50 x 50 -> {ConstructConveyor.SizeRefusal(line, k, 50, 50) ?? "ok"}");
        line.Routes[k].ConvW = line.Routes[k].ConvH = 100;
        Cut(0);

        // 5. Cost: 20 conveyors of 10 riders each (200), timed on the host's tick.
        var extra = new List<int>();
        for (var j = 0; j < 20; j++)
        {
            CRouteNode[] loop = [new() { Pos = ConstructWorld.Quantise(p0 + new Vector3(0, 0.02f * j, 8)), Host = (ushort)fnd }, new() { Pos = ConstructWorld.Quantise(p0 + new Vector3(0, 0.02f * j, 16)), Host = (ushort)fnd },
                new() { Pos = ConstructWorld.Quantise(p0 + new Vector3(0, 0.02f * j, 24)), Host = (ushort)fnd }, new() { Pos = ConstructWorld.Quantise(p0 + new Vector3(0, 0.02f * j, 32)), Host = (ushort)fnd }];
            extra.Add(ConstructRoutes.Add(w, line, RouteType.Conveyor, ei, 255, 255, 0, default, loop, rack, 255));
        }
        line.Rebuild();
        var beam = Extrusion.Def(new ExtrusionKey(Extrusion.Presets[4].Mask, 25, 25, 200, metal, false))!;
        foreach (var r in extra)
            for (var q = 0; q < 10; q++)
            {
                line.ReserveConveyed(line.ConveyedCount + 1);
                line.Conveyed[line.ConveyedCount++] = new CRider { Def = beam.Id, Route = (byte)r, At = 70 - q * 2.5f, Health = 255 };
            }
        w.RidersChanged(line);
        Console.WriteLine($"[probe] cost run: {line.ConveyedCount} riders on {extra.Count} conveyors");
        var tickTimes = new List<double>();
        var stepTimes = new List<double>();
        var sw = new Stopwatch();
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        long stepAlloc = 0;
        for (var t = 0; t < 600; t++)
        {
            sw.Restart();
            w.Step(World.Query, World.TickDt);
            tickTimes.Add(sw.Elapsed.TotalMilliseconds);
            if (t % ConstructConveyor.Every == 0)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                w.ConveyorTicks = ConstructConveyor.Every - 1;
                ConstructConveyor.Step(w);
                stepTimes.Add(sw.Elapsed.TotalMilliseconds);
                stepAlloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            }
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        tickTimes.Sort();
        stepTimes.Sort();
        Console.WriteLine($"[probe] host world tick with {line.ConveyedCount} riders left: median {tickTimes[300]:0.000} ms, p99 {tickTimes[594]:0.000} ms, max {tickTimes[599]:0.000} ms; conveyor step alone median {stepTimes[stepTimes.Count / 2]:0.0000} ms, max {stepTimes[^1]:0.0000} ms; {alloc} bytes allocated over 600 ticks ({stepAlloc} in the conveyor steps); rack {rs.ItemCount}");
        foreach (var r in extra.OrderByDescending(x => x))
            ConstructRoutes.Remove(w, line, r);
        line.Rebuild();
        Pump(server, [a], 1, ref now, clock);

        // 6. A save with riders: two on the link, then the world saved and read back into another game.
        Cut(2);
        Until(() => line.ConveyedCount >= 2, 60);
        Cut(0);
        var savedRiders = line.ConveyedCount;
        var savedRack = rs.ItemCount;
        var savedSize = (line.Routes[k].ConvW, line.Routes[k].ConvH);
        Console.WriteLine($"[probe] save: {(server.WorldSaveNow(true) ? "saved" : "NOT SAVED")} with {savedRiders} riding, rack {savedRack}, size {savedSize}");
        a.Dispose();
        server.Dispose();
        var entry = WorldStore.List().FirstOrDefault(e => e.Folder == folder);
        if (entry != null && Game.WorldOpen(entry, out _) is { } back)
        {
            using var g2 = ModeGame(0, GameMode.Sandbox, yard.Id);
            g2.WorldAttach(back);
            Run(g2, ref now, 0.5);
            var found = g2.Constructs.Bodies.FirstOrDefault(b => PartNamed(b, "Piece Rack") >= 0);
            var rk = found == null ? -1 : ConstructConveyor.Out(found, PartNamed(found, "Extruder"));
            var rack2 = found == null ? null : g2.Constructs.StoreOf(found, PartNamed(found, "Piece Rack"));
            Console.WriteLine($"[probe] loaded: {(found == null ? "no line" : $"{found.ConveyedCount} riding, rack {rack2?.ItemCount} ({(rack2 != null ? Kinds(rack2) : "")}), size {(rk >= 0 ? $"{found.Routes[rk].ConvW} x {found.Routes[rk].ConvH}" : "no conveyor")}")}");
        }
        else
            Console.WriteLine("[probe] the save isn't listed");
        WorldStore.Delete(folder);
        Console.WriteLine($"[probe] {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
        return 0;
    }
}
