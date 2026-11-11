using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// TEMPORARY PROBE (fix/rack-volume): removed before the final push.
public static partial class SelfTest
{
    public static int ProbeRack(string[] args)
    {
        SavePaths.Init(args);
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find("Proving Ground") is not { } map)
            return 1;
        const ushort port = 26441;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        var folder = WorldStore.FolderFor("Rack probe");
        WorldStore.Delete(folder);
        var session = Game.WorldNew("Rack probe", map, false);
        var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        server.WorldAttach(session);
        var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        void Run(double seconds) => Pump(server, [a], seconds, ref now, clock);
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
        Console.WriteLine($"[probe] spawn: {server.SandboxSpawnBlueprint(player, index) ?? "asked"}");
        Construct? c = null;
        for (var k = 0; k < 200 && c == null; k++)
        {
            Run(0.25);
            c = w.Bodies.FirstOrDefault(b => b.PartCount > 200 && Enumerable.Range(0, b.PartCount).Any(i => b.DefOf(i).Name == "Line Console"));
        }
        if (c == null)
            return 1;
        int Nth(string name, int n = 0) => Enumerable.Range(0, c!.PartCount).Where(i => c.DefOf(i).Name == name).Skip(n).First();
        var rack = Nth("Piece Rack");
        var lrack = Nth("Long Piece Rack");
        var rst = w.StoreOf(c, rack)!;
        var lst = w.StoreOf(c, lrack)!;
        var consoles = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == "Line Console").ToArray();
        foreach (var con in consoles)
            c.Parts[con].State = 1;
        var orders = consoles.Length > 1 ? consoles[1] : consoles[0];
        Console.WriteLine($"[probe] send: {WorkOrders.BillSend(w, c, orders) ?? "ok"}");
        var bracket = ConstructParts.Need("L Bracket");
        var metal = RecyclingTable.Current.IdOf("metal");
        var beam = Extrusion.Def(new ExtrusionKey(ulong.MaxValue, 20, 20, 400, metal, false))!;
        Console.WriteLine($"[probe] bracket {bracket.BoundsMax - bracket.BoundsMin}, beam {beam.BoundsMax - beam.BoundsMin}");

        // 1. The line runs: the press fills the bracket rack (60 s), then the rest is put in directly.
        for (var s = 0; s < 6; s++)
        {
            Run(10);
            Console.WriteLine($"[probe] t+{(s + 1) * 10}: bracket rack {ConstructConveyor.Look(c.DefOf(rack), rst, false)}; long rack {ConstructConveyor.Look(c.DefOf(lrack), lst, false)}; riders {c.ConveyedCount}");
        }
        string Refuse(int part, CStore st, ConstructPartDef d) => ConstructConveyor.RackRefusal(c!, part, st, d) ?? "takes it";
        void Empty(int part, CStore st)
        {
            while (st.KindN > 0 && ConstructRackPack.LastOf(st, st.KindDef![0], out var s, out var l))
                w.RackTakeAt(c!, part, s, l);
        }
        int Fill(int part, ConstructPartDef d)
        {
            var n = 0;
            while (w.RackPut(c!, part, d))
                n++;
            return n;
        }
        var had = rst.ItemCount;
        var more = Fill(rack, bracket);
        Console.WriteLine($"[probe] Piece Rack: {had} from the press + {more} = {rst.ItemCount} brackets; {ConstructConveyor.FillText(c.DefOf(rack), rst)}; next: {Refuse(rack, rst, bracket)}");
        Empty(lrack, lst);
        Console.WriteLine($"[probe] Long Piece Rack brackets: {Fill(lrack, bracket)}; {ConstructConveyor.FillText(c.DefOf(lrack), lst)}");
        Empty(lrack, lst);
        Console.WriteLine($"[probe] Long Piece Rack 4 m beams: {Fill(lrack, beam)}; {ConstructConveyor.FillText(c.DefOf(lrack), lst)}; a bracket next: {Refuse(lrack, lst, bracket)}");
        Empty(lrack, lst);
        Empty(rack, rst);
        Console.WriteLine($"[probe] Piece Rack 4 m beams: {Fill(rack, beam)}; brackets after them: {Fill(rack, bracket)}; {ConstructConveyor.FillText(c.DefOf(rack), rst)}");
        var longBeam = Extrusion.Def(new ExtrusionKey(ulong.MaxValue, 20, 20, 700, metal, false))!;
        Console.WriteLine($"[probe] a 7 m beam into the Piece Rack: {Refuse(rack, rst, longBeam)}");
        // Mixed on the long rack: 10 beams, 200 brackets, 6 more beams, then a kind out and brackets in its gap.
        for (var k = 0; k < 10; k++) w.RackPut(c, lrack, beam);
        for (var k = 0; k < 200; k++) w.RackPut(c, lrack, bracket);
        for (var k = 0; k < 6; k++) w.RackPut(c, lrack, beam);
        Console.WriteLine($"[probe] mixed long rack: {ConstructConveyor.Look(c.DefOf(lrack), lst, false)}; columns {lst.SlabN}");
        Run(1.0);
        var ac = a.Constructs.Get(c.Id);
        CStore? Client(int part) => ac == null ? null : a.Constructs.StoreOf(ac, part);
        Console.WriteLine($"[probe] places hash long: server {ConstructRackPack.PlacesHash(lst):x} client {(Client(lrack) is { } cl ? ConstructRackPack.PlacesHash(cl).ToString("x") : "none")}; short: server {ConstructRackPack.PlacesHash(rst):x} client {(Client(rack) is { } cr ? ConstructRackPack.PlacesHash(cr).ToString("x") : "none")}");

        // 2. Ten taken at random: nothing else moves.
        HashSet<(ushort, Vector3)> Places(CStore st, ConstructPartDef rd)
        {
            var set = new HashSet<(ushort, Vector3)>();
            for (var s = 0; s < st.SlabN; s++)
            {
                var d = ConstructParts.Def(st.Slabs![s].Def);
                for (var l = 0; l < st.Slabs[s].Lanes; l++)
                    for (var y = 0; y < st.SlabLanes![s * ConstructRackPack.MaxLanes + l]; y++)
                        set.Add((d.Id, ConstructRackPack.Origin(rd, st.Slabs[s], d, l, y)));
            }
            return set;
        }
        var before = Places(lst, c.DefOf(lrack));
        var rng = new Random(7);
        for (var k = 0; k < 10; k++)
        {
            var s = rng.Next(lst.SlabN);
            var l = rng.Next(lst.Slabs![s].Lanes);
            if (lst.SlabLanes![s * ConstructRackPack.MaxLanes + l] == 0)
            {
                k--;
                continue;
            }
            w.RackTakeAt(c, lrack, s, l);
        }
        var after = Places(lst, c.DefOf(lrack));
        Console.WriteLine($"[probe] ten taken: {before.Count} -> {after.Count} pieces; moved {after.Count(p => !before.Contains(p))}");
        // Brackets into the gaps the beams' column left.
        var gapBefore = lst.SlabN;
        for (var k = 0; k < 16 && ConstructRackPack.LastOf(lst, beam.Id, out var bs, out var bl); k++)
            w.RackTakeAt(c, lrack, bs, bl);
        var stay = Places(lst, c.DefOf(lrack));
        var put = Fill(lrack, bracket);
        var after2 = Places(lst, c.DefOf(lrack));
        Console.WriteLine($"[probe] beams out ({gapBefore} -> {lst.SlabN} columns), {put} brackets in: old places kept {stay.All(after2.Contains)}; {ConstructConveyor.FillText(c.DefOf(lrack), lst)}");

        // 3. By hand: F (UseBin) on the rack, Shift+F, a rider off its conveyor, the physics gun on both.
        var hands = server.SandboxHands;
        c.PartBounds(rack, out var mn, out var mx);
        c.WorldFrame(c.Parts[rack].Seg, out var rq, out var rt);
        var mid = rt + Vector3.Transform((mn + mx) * 0.5f, rq);
        var eye = mid + new Vector3(0, 1.6f, 0) - Vector3.Transform(Vector3.UnitX, rq * Rot24.Quat(c.Parts[rack].Rot)) * 1.2f;
        var look = Vector3.Normalize(mid - eye);
        var hitOk = w.Raycast(eye, look, 6, out var hit, look: true);
        Console.WriteLine($"[probe] look ray at the bracket rack: {(hitOk ? $"{hit.Body.DefOf(hit.Part).Name}, stack {hit.RackPlus1}/{hit.RackLane}, {hit.Distance:0.00} m" : "nothing")}");
        var n0 = rst.ItemCount;
        hands.Hand(player.Id).Held = ConstructParts.None;
        Console.WriteLine($"[probe] F: {hands.UseBin(w, player.Id, eye, c.Id, rack, look) ?? "ok"}; {n0} -> {rst.ItemCount}; held {ConstructParts.Get(hands.Hand(player.Id).Held)?.Name ?? "nothing"}");
        var bodies = w.Bodies.Count;
        Console.WriteLine($"[probe] Shift+F: {hands.RackDrop(w, player.Id, eye, look, c.Id, rack) ?? "ok"}; {rst.ItemCount} left; bodies {bodies} -> {w.Bodies.Count}");
        Run(1.0);
        Console.WriteLine($"[probe] client after: {(Client(rack) is { } cr2 ? $"{cr2.ItemCount} pieces, hash {ConstructRackPack.PlacesHash(cr2) == ConstructRackPack.PlacesHash(rst)}" : "none")}");
        // A rider: wait for one, take it.
        for (var k = 0; k < 40 && c.ConveyedCount == 0; k++)
            Run(0.5);
        if (c.ConveyedCount > 0)
        {
            var r = c.Conveyed[0];
            var d = ConstructParts.Def(r.Def);
            ConstructConveyor.Pose(c, r.Route, r.At - 0.1f, d, out var o, out _, out var seg);
            c.WorldFrame(seg, out var q, out var t);
            var at = t + Vector3.Transform(o, q);
            var rEye = at + new Vector3(0, 1.5f, 0.5f);
            var rhit = a.Constructs.Get(c.Id) is { } cc && a.Constructs.Raycast(rEye, Vector3.Normalize(at - rEye), 6, out var h2, look: true) ? $"client ray: rider {h2.RiderPlus1}, route {h2.RoutePlus1}" : "client ray: nothing";
            var n1 = c.ConveyedCount;
            hands.Hand(player.Id).Held = ConstructParts.None;
            Console.WriteLine($"[probe] rider {d.Name} at {r.At:0.00} m; {rhit}; take: {hands.TakeRider(w, player.Id, rEye, 0, c.Id, r.Route, (int)(r.At * 100) - 20, false) ?? "ok"}; riders {n1} -> {c.ConveyedCount}; held {ConstructParts.Get(hands.Hand(player.Id).Held)?.Name ?? "nothing"}");
        }
        else
            Console.WriteLine("[probe] no rider came");
        Console.WriteLine($"[probe] jam texts: {string.Join("; ", Enumerable.Range(0, c.RouteCount).Where(k => c.Routes[k].Type == RouteType.Conveyor).Select(k => ConstructConveyor.JamText(c, k, ConstructConveyor.Jam(w, c, k)) ?? "moving"))}");

        void Ticks(string what)
        {
            var ticks = new List<double>();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 600; k++)
            {
                var t0 = Stopwatch.GetTimestamp();
                server.Update(now += World.TickDt, World.TickDt);
                ticks.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                if (k % 6 == 0)
                    a.Update(now, World.TickDt * 6);
            }
            ticks.Sort();
            Console.WriteLine($"[probe] server ticks, {what}: median {ticks[300]:0.00} ms, p99 {ticks[594]:0.00}, worst {ticks[^1]:0.00}; {(GC.GetAllocatedBytesForCurrentThread() - b0) / 1024} KB over 600 (both peers)");
        }
        // 4. Cost: 20 full racks (the two and 18 more filled the same way, on a second showcase is too big: fill these 20 times over).
        Fill(rack, bracket);
        Fill(lrack, bracket);
        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var hits = 0;
        for (var k = 0; k < 600; k++)
            if (w.Raycast(eye, look, 6, out _, look: true))
                hits++;
        Console.WriteLine($"[probe] 600 look rays at a full rack: {sw.Elapsed.TotalMilliseconds / 600:0.0000} ms each, {GC.GetAllocatedBytesForCurrentThread() - a0} bytes");
        Ticks("full racks");
        Empty(rack, rst);
        Empty(lrack, lst);
        Run(1.0);
        Ticks("empty racks");
        Fill(rack, bracket);
        Fill(lrack, bracket);
        Ticks("full again");
        Run(1.0);
        Console.WriteLine($"[probe] client full: {(Client(lrack) is { } cl3 ? $"{cl3.ItemCount}, hash ok {ConstructRackPack.PlacesHash(cl3) == ConstructRackPack.PlacesHash(lst)}" : "none")}");

        // 5. Save and load.
        var savedLong = (lst.ItemCount, lst.KindN, ConstructConveyor.FillText(c.DefOf(lrack), lst));
        var savedShort = (rst.ItemCount, rst.KindN);
        Console.WriteLine($"[probe] save: {server.WorldSaveNow(true)}");
        Run(0.5);
        Console.WriteLine($"[probe] {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
        a.Dispose();
        server.Dispose();
        var entry = WorldStore.List().FirstOrDefault(e => e.Folder == folder);
        if (entry != null && Game.WorldOpen(entry, out var why) is { } back)
        {
            using var g2 = ModeGame(0, GameMode.Sandbox, map.Id);
            g2.WorldAttach(back);
            Pump(g2, [], 0.5, ref now, clock);
            var again = g2.Constructs.Bodies.FirstOrDefault(b => b.PartCount > 200 && Enumerable.Range(0, b.PartCount).Any(i => b.DefOf(i).Name == "Line Console"));
            if (again != null)
            {
                int N2(string name) => Enumerable.Range(0, again.PartCount).First(i => again.DefOf(i).Name == name);
                var l2 = g2.Constructs.StoreOf(again, N2("Long Piece Rack"))!;
                var s2 = g2.Constructs.StoreOf(again, N2("Piece Rack"))!;
                Console.WriteLine($"[probe] loaded long: {l2.ItemCount} pieces {l2.KindN} kinds, {ConstructConveyor.FillText(again.DefOf(N2("Long Piece Rack")), l2)} (saved {savedLong}); short {s2.ItemCount}/{s2.KindN} (saved {savedShort}); columns {l2.SlabN}");
            }
            else
                Console.WriteLine("[probe] loaded: no showcase");
        }
        else
            Console.WriteLine("[probe] no save to load");
        WorldStore.Delete(folder);
        return 0;
    }
}
