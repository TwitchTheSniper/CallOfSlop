using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A part fitted by its socket <paramref name="by"/> onto the first free structural socket of the construct that takes it.</summary>
    private static int ModFit(ConstructWorld w, Construct c, string part, string by, string what)
    {
        var def = ConstructParts.Need(part);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
        for (var on = 0; on < c.PartCount; on++)
        {
            var od = c.DefOf(on);
            for (var s = 0; s < od.Sockets.Length; s++)
                if (od.Sockets[s].Kind == SocketKind.Struct && !c.SocketUsed(on, s) && ConstructBuild.Place(c, on, s, def, ds, 0, out var g) == null)
                {
                    var i = ConstructBuild.Attach(w, c, def, g);
                    c.Rebuild();
                    return i;
                }
        }
        Fail($"{what}: a {part} doesn't fit anywhere on the {c.DefOf(0).Name}");
        return -1;
    }

    private static float PartSum(Construct c)
    {
        var m = 0f;
        for (var i = 0; i < c.PartCount; i++)
            m += c.PartMass(i);
        return m;
    }

    /// <summary>
    /// "Bin loads": a bulk bin's load counts in its construct's mass, centre of mass and inertia. The weights are read and
    /// bad ones refused; a filled bin on a buggy (the real Deposit) changes the mass and centre of mass by the sums, with no
    /// rebuild, the cheap path giving exactly what a rebuild gives; the full buggy leans and drives slower than its empty
    /// twin; the mass comes back after F takes it, tips it in, a chute (same body and another), a clear, a broken bin's spill
    /// and a split; a load on a joint's leaf and on a freeform weld count; a parked body wakes; the load step allocates
    /// nothing and is throttled.
    /// </summary>
    private static void CheckBinLoads()
    {
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // ---- The table.
        var table = RecyclingTable.Current;
        var metal = table.IdOf("metal");
        var ore = table.IdOf("iron ore");
        Want(table.Problem.Length == 0 && table.WeightOf(metal) == 2f && table.WeightOf(ore) == 4f && table.WeightOf(0) == table.DefaultKgPerUnit, $"the shipped weights (\"{table.Problem}\", metal {table.WeightOf(metal)}, ore {table.WeightOf(ore)})");
        foreach (var m in table.Materials)
            Want(table.KgPerUnit.ContainsKey(m), $"the table gives no weight for {m}");
        const string head = "{\"format\": \"call-of-slop-recycling/1\", \"materials\": [\"metal\", \"wood\"], \"classes\": {}, \"roles\": {}, ";
        var own = RecyclingTable.Parse(head + "\"defaultKgPerUnit\": 3, \"kgPerUnit\": {\"metal\": 7}}");
        Want(own.Problem.Length == 0 && own.WeightOf(1) == 7f && own.WeightOf(2) == 3f, $"a listed weight and the default (\"{own.Problem}\", {own.WeightOf(1)}, {own.WeightOf(2)})");
        var jam = RecyclingTable.Parse(head + "\"kgPerUnit\": {\"jam\": 1}}");
        var zero = RecyclingTable.Parse(head + "\"kgPerUnit\": {\"metal\": 0}}");
        var word = RecyclingTable.Parse(head + "\"kgPerUnit\": {\"metal\": \"heavy\"}}");
        var list = RecyclingTable.Parse(head + "\"kgPerUnit\": [1]}");
        var deflt = RecyclingTable.Parse(head + "\"defaultKgPerUnit\": 500}");
        Want(jam.Problem.Contains("jam") && zero.Problem.Contains("kgPerUnit.metal") && word.Problem.Contains("kgPerUnit.metal") && list.Problem.Contains("kgPerUnit") && deflt.Problem.Contains("defaultKgPerUnit"),
            $"bad weights are refused with a reason (\"{jam.Problem}\" / \"{zero.Problem}\" / \"{word.Problem}\" / \"{list.Problem}\" / \"{deflt.Problem}\")");

        // ---- A buggy with a bin, filled, against its empty twin.
        if (DriveBlueprint("Buggy") is not { } buggy)
            return;
        var w = new ConstructWorld();
        var q = new DriveWorld();
        void Steps(int n)
        {
            for (var t = 0; t < n; t++)
                w.Step(q, World.TickDt);
        }
        void Load() => Steps(ConstructWorld.LoadEvery + 1);
        if (DriveSpawn(w, buggy, 0, 0, MathF.PI / 2, "the loaded buggy") is not { } car || DriveSpawn(w, buggy, 0, 30, MathF.PI / 2, "the empty buggy") is not { } twin)
            return;
        car.Owner = twin.Owner = 255;
        var bin = ModFit(w, car, "Materials Bin", "bottom", "bin loads");
        var tb = ModFit(w, twin, "Materials Bin", "bottom", "bin loads");
        if (bin < 0 || tb < 0)
            return;
        var binDef = ConstructParts.Need("Materials Bin");
        Steps(120);
        var mass0 = car.Mass;
        var com0 = car.Com;
        var inv0 = car.InvInertiaLocal;
        Want(car.HasBin && car.LoadMass == 0 && MathF.Abs(mass0 - PartSum(car)) < 0.01f, $"an empty bin adds nothing ({mass0} against {PartSum(car)}, load {car.LoadMass})");
        var put = w.Deposit(car, bin, binDef.BulkCapacity, metal);
        Want(put == binDef.BulkCapacity, $"the deposit went in ({put})");
        Load();
        var load = put * table.WeightOf(metal);
        ConstructStorage.FillBox(car.Parts[bin], binDef, put, out var fill, out _);
        var expect = (com0 * mass0 + fill * load) / (mass0 + load);
        Line($"bin loads: buggy {mass0:0} kg, com {com0}; {put} metal = {load:0} kg at {fill}: {car.Mass:0} kg, com {car.Com} (want {expect})");
        Want(MathF.Abs(car.Mass - (mass0 + load)) < 0.05f && car.LoadMass == load, $"a full bin's mass ({car.Mass}, want {mass0 + load})");
        Want(Vector3.Distance(car.Com, expect) < 0.002f, $"a full bin's centre of mass ({car.Com}, want {expect})");
        Want(Vector3.Distance(car.Com, com0) > 0.05f, $"the centre of mass didn't move ({Vector3.Distance(car.Com, com0):0.000} m)");
        Want(car.InvInertiaLocal != inv0, "the inertia didn't follow the load");
        Want(MathF.Abs(car.InvMass - 1f / car.Mass) < 1e-7f, "the inverse mass didn't follow");
        Want(!car.Dirty, "a deposit asked for a full rebuild");
        // The cheap path is the rebuild's result.
        var (ma, ca, ia, sa, ra) = (car.Mass, car.Com, car.InvInertiaLocal, car.SegMass[0], car.Radius);
        car.Dirty = true;
        car.Rebuild();
        Want(ma == car.Mass && ca == car.Com && ia == car.InvInertiaLocal && sa == car.SegMass[0] && ra == car.Radius, $"RefreshMass and Rebuild differ ({ma}/{car.Mass}, {ca}/{car.Com}, radius {ra}/{car.Radius})");
        var w0 = w.LoadRefreshes;
        Steps(60); // (nothing changed: no refresh)
        Want(w.LoadRefreshes == w0, "the load step redid an unchanged body");

        // ---- It reaches the physics: it leans towards the load, and it drives slower.
        Steps(180);
        Vector3 Up(Construct c) => Vector3.Transform(Vector3.UnitY, c.Rot);
        var lean = MathF.Acos(Math.Clamp(Vector3.Dot(Up(car), Up(twin)), -1, 1)) * 180 / MathF.PI;
        var side = Vector3.Transform(car.Com - com0, car.Rot) with { Y = 0 };
        var leansThere = Vector3.Dot(Up(car) - Up(twin), side);
        Line($"bin loads: the full buggy stands {lean:0.00} degrees off its twin, towards the load {leansThere:0.0000}");
        Want(lean > 0.1f && leansThere > 0, $"the full buggy doesn't lean towards its load ({lean:0.00} degrees, {leansThere:0.0000})");
        var start = car.WorldCom;
        var startT = twin.WorldCom;
        Seat(car);
        Seat(twin);
        for (var t = 0; t < 240; t++)
        {
            DriveKeys(car, Buttons.Forward);
            DriveKeys(twin, Buttons.Forward);
            w.Step(q, World.TickDt);
        }
        var d1 = Vector3.Distance(start with { Y = 0 }, car.WorldCom with { Y = 0 });
        var d0 = Vector3.Distance(startT with { Y = 0 }, twin.WorldCom with { Y = 0 });
        Line($"bin loads: 4 s at full throttle: empty {d0:0.0} m, full {d1:0.0} m (rpm {twin.Rpm:0} / {car.Rpm:0})");
        Want(d1 < d0 * 0.8f && d1 > 1f, $"a full buggy doesn't slow down ({d1:0.0} m against {d0:0.0} m)");
        for (var t = 0; t < 240; t++)
        {
            DriveKeys(car, Buttons.Jump);
            DriveKeys(twin, Buttons.Jump);
            w.Step(q, World.TickDt);
        }
        car.Driver = twin.Driver = 255;
        car.DriverSeat = twin.DriverSeat = -1;
        Steps(120);

        // ---- Every way out.
        var e = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        e.Inventory.SetInfinite(false);
        const byte me = 5;
        var inv = e.Inventory.Of(me);
        Vector3 Eye(Construct c, int part)
        {
            c.WorldFrame(c.Parts[part].Seg, out var fq, out var ft);
            c.PartBounds(part, out var mn, out var mx);
            return ft + Vector3.Transform((mn + mx) * 0.5f, fq) + new Vector3(0, 0.5f, 0);
        }
        var st = w.StoreOf(car, bin)!;
        var took = e.UseBin(w, me, Eye(car, bin), car.Id, bin);
        Load();
        Want(took == null && st.Amount == put - ConstructStorage.TakeLoad && MathF.Abs(car.Mass - (mass0 + load - ConstructStorage.TakeLoad * 2f)) < 0.05f, $"F took a load ({took}; {st.Amount}, {car.Mass} kg)");
        for (var guard = 0; guard < 10 && st.Amount > 0; guard++)
        {
            inv.TakeMaterial(metal, inv.Materials[metal]); // (F tips in what you carry: pockets empty, it takes a load)
            e.UseBin(w, me, Eye(car, bin), car.Id, bin);
        }
        Load();
        Want(st.Amount == 0 && MathF.Abs(car.Mass - mass0) < 0.05f && Vector3.Distance(car.Com, com0) < 0.002f && car.LoadMass == 0, $"the mass isn't back after F took it all ({car.Mass}, {mass0}; com {car.Com}, {com0})");
        inv.AddMaterial(metal, put - inv.Materials[metal]);
        var tipped = e.UseBin(w, me, Eye(car, bin), car.Id, bin);
        Load();
        Want(tipped == null && inv.Materials[metal] == 0 && MathF.Abs(car.Mass - (mass0 + load)) < 0.05f, $"F tipped it back in ({tipped}; {car.Mass} kg)");
        // A chute between two bins of one body: the mass stays, the centre of mass moves.
        var bin2 = ModFit(w, car, "Materials Bin", "bottom", "bin loads");
        Steps(5);
        var mass2 = car.Mass;
        var com2 = car.Com;
        var route = ConstructRoutes.Add(w, car, RouteType.Chute, bin, 255, 255, 0, default, default, bin2, 255);
        car.Rebuild();
        Want(route >= 0, "the chute between two bins was refused");
        Steps(150);
        Load();
        Want(w.StoreOf(car, bin2)!.Amount > 0, "the chute moved nothing");
        Want(MathF.Abs(car.Mass - mass2) < 0.05f, $"a chute on one body changed its mass ({car.Mass}, {mass2})");
        Want(Vector3.Distance(car.Com, com2) > 0.001f, $"a chute between two bins left the centre of mass alone ({car.Com})");
        ConstructRoutes.Remove(w, car, route);
        // A chute to another body (a world link's move): one goes down as the other comes up.
        var twinMass = twin.Mass;
        var moved = ConstructRoutes.ChuteMove(w, car, bin, twin, tb);
        Load();
        Want(moved && MathF.Abs(twin.Mass - (twinMass + ConstructRoutes.ChuteLoad * 2f)) < 0.05f && MathF.Abs(car.Mass - (mass2 - ConstructRoutes.ChuteLoad * 2f)) < 0.05f, $"a chute between bodies ({car.Mass}, {twin.Mass})");
        // Cleared.
        w.Withdraw(twin, tb, 100000);
        Load();
        Want(MathF.Abs(twin.Mass - twinMass) < 0.05f && twin.LoadMass == 0, $"a cleared bin's mass ({twin.Mass}, {twinMass})");
        // A broken bin spills: the body keeps nothing of it.
        var bodies0 = w.Bodies.Count;
        w.DamagePart(car, bin, 1e6f, out _, raw: true);
        Steps(ConstructWorld.LoadEvery + 2);
        Want(w.Bodies.Count > bodies0, "the broken bin spilled no bundles");
        var left = 0f;
        for (var i = 0; i < car.PartCount; i++)
            left += w.StoreOf(car, i)?.Amount * 2f ?? 0f;
        Want(MathF.Abs(car.LoadMass - left) < 0.05f && left > 0, $"the broken bin's load stayed ({car.LoadMass}, the other bin holds {left})");
        Want(MathF.Abs(car.Mass - (PartSum(car) + car.LoadMass)) < 0.05f, $"after the spill: {car.Mass} kg, parts {PartSum(car)} + load {car.LoadMass}");

        // ---- A split: the load goes with its part. A load on a joint's leaf; on a freeform weld.
        var chain = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Chain", "steps": [
              { "part": "Frame Beam" },
              { "part": "Frame Beam", "on": 0, "socket": "x+0", "by": "x-0" },
              { "part": "Materials Bin", "on": 1, "socket": "y+1", "by": "bottom" }
            ] }
            """, "chain.json"), new Vector3(30, 4, 0), Quaternion.Identity, out var chainWhy);
        if (chain == null)
        {
            Fail($"bin loads: the chain doesn't build: {chainWhy}");
            return;
        }
        chain.Owner = 255;
        w.Deposit(chain, 2, 300, ore);
        Steps(ConstructWorld.LoadEvery + 1);
        Want(MathF.Abs(chain.LoadMass - 1200f) < 0.05f, $"the chain's ore ({chain.LoadMass})");
        var keepMass = chain.DefOf(0).Mass;
        w.RemovePart(chain, 1);
        Steps(2);
        var piece = w.Bodies.FirstOrDefault(b => b.PartCount == 1 && b.DefOf(0) == binDef && w.StoreOf(b, 0) is { Amount: 300 });
        Want(piece != null && MathF.Abs(piece.Mass - (binDef.Mass + 1200f)) < 0.05f, $"the split-off bin's mass ({piece?.Mass})");
        Want(MathF.Abs(chain.Mass - keepMass) < 0.05f && chain.LoadMass == 0, $"what stayed keeps the load ({chain.Mass})");
        var arm = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Arm", "steps": [
              { "part": "Frame Beam" },
              { "part": "Hinge", "on": 0, "socket": "y+1", "by": "base" },
              { "part": "Materials Bin", "on": 1, "socket": "leaf", "by": "bottom" }
            ] }
            """, "arm.json"), new Vector3(-30, 4, 0), Quaternion.Identity, out var armWhy);
        if (arm == null)
        {
            Fail($"bin loads: the arm doesn't build: {armWhy}");
            return;
        }
        arm.Pinned = true;
        arm.Rebuild();
        var seg = arm.Parts[2].Seg;
        var segMass = arm.SegMass[seg];
        w.Deposit(arm, 2, 300, metal);
        Steps(ConstructWorld.LoadEvery + 1);
        Want(seg != 0 && MathF.Abs(arm.SegMass[seg] - (segMass + 600f)) < 0.05f, $"a load on a joint's leaf (segment {seg}: {arm.SegMass[seg]}, was {segMass})");
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var pos = new Vector3(0, 1.2f, 0.4f);
        var weld = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint($$"""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Weld", "steps": [
              { "part": "Frame Beam" },
              { "part": "Materials Bin", "weld": {{ConstructBuild.WeldJson(0, pos, rot)}} }
            ] }
            """, "weld.json"), new Vector3(-30, 4, 30), Quaternion.Identity, out var weldWhy);
        if (weld == null)
        {
            Fail($"bin loads: the welded bin doesn't build: {weldWhy}");
            return;
        }
        weld.Pinned = true;
        weld.Rebuild();
        var (wm, wc) = (weld.Mass, weld.Com);
        w.Deposit(weld, 1, 600, metal);
        Steps(ConstructWorld.LoadEvery + 1);
        ConstructStorage.FillBox(weld.Parts[1], binDef, 600, out var wf, out _);
        var wx = (wc * wm + (pos + Vector3.Transform(wf, rot)) * 1200f) / (wm + 1200f);
        Want(MathF.Abs(weld.Mass - (wm + 1200f)) < 0.05f && Vector3.Distance(weld.Com, wx) < 0.01f, $"a load on a freeform weld ({weld.Mass}, com {weld.Com}, want {wx})");

        // ---- A parked body wakes; the step is throttled and allocates nothing.
        var slept = SpUntil(w, q, 900, () => twin.Asleep);
        Want(slept >= 0, "the twin never slept");
        w.Deposit(twin, tb, 100, metal);
        Load();
        Want(!twin.Asleep && MathF.Abs(twin.Mass - (twinMass + 200f)) < 0.05f, $"a load on a parked body didn't wake it (asleep {twin.Asleep}, {twin.Mass} kg)");
        SpRun(w, q, 60);
        var refreshes = w.LoadRefreshes;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 180; t++)
        {
            if (t % ConstructGatherer.Every == 0)
            {
                w.Deposit(twin, tb, 1, metal);
                w.Withdraw(arm, 2, 1);
            }
            w.Step(q, World.TickDt);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        refreshes = w.LoadRefreshes - refreshes;
        Line($"bin loads: 180 ticks of deposits at 10 Hz on two bodies: {refreshes} refreshes, {bytes} B");
        Want(bytes == 0, $"the load step allocates ({bytes} B)");
        Want(refreshes is >= 2 and <= (180 / ConstructWorld.LoadEvery + 1) * 2, $"the load step isn't throttled ({refreshes} refreshes in 180 ticks)");
        Want(!twin.Dirty && !arm.Dirty, "a deposit left a body dirty");
        foreach (var p in problems)
            Fail($"bin loads: {p}");
    }

    /// <summary>A module pinned to its work spot with a battery and a switch on it (no seat, no vehicle): the part index of each.</summary>
    private static Construct? ModRig(ConstructWorld w, string part, Vector3 workAt, out int switchPart)
    {
        switchPart = -1;
        var c = GatherBody(w, part, workAt);
        c.Pinned = true;
        c.Owner = 255;
        c.Rebuild();
        if (Fit(w, c, 0, "-x", "Battery", "+x") < 0)
            return null;
        switchPart = Fit(w, c, 0, "+x", "Switch", "-x");
        return switchPart < 0 ? null : c;
    }

    /// <summary>
    /// "Module Run input": Saw and Drill Modules and the Recycler have one data input, "Run". Nothing wired, they run as
    /// before; wired, only while the signal is on: driven through the real logic step on a pinned base with no seat, in
    /// survival with its battery. Held off, a module doesn't gather, doesn't draw, and says so on the look line; a cut
    /// cable holds it off and a mended one lets it go; a deleted link is unwired again. A Recycler held off takes nothing
    /// in, and with a job in hand waits without drawing, then finishes it. Blueprints wire it, by links and by wires.
    /// </summary>
    private static void CheckModuleRun()
    {
        if (Maps.Find(GatherMap) is not { } map || map.Sandbox == null || Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail("module run: the Sandbox maps are missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // ---- The ports.
        Span<ConstructRoutes.LinkPort> ports = new ConstructRoutes.LinkPort[32];
        foreach (var name in (string[])["Saw Module", "Drill Module", "Recycler"])
        {
            var d = ConstructParts.Need(name);
            Want(d.MaxInputs == 1 && d.DataIn is [ConstructGatherer.RunPort], $"the {name}'s data input ({d.MaxInputs}, {string.Join("/", d.DataIn)})");
            Want(ConstructRoutes.IsInput(d, RouteType.Data) && !ConstructRoutes.IsOutput(d, RouteType.Data), $"the {name} isn't a data input only");
            Want(ConstructParts.PortProblems(d).Count == 0, $"the {name}'s ports: {string.Join("; ", ConstructParts.PortProblems(d))}");
        }
        Want(ConstructParts.Need("Materials Bin").MaxInputs == 0, "a plain bin takes a wire");

        map.RestoreProps();
        World.SetMap(map);
        var f = map.Resources!;
        var w = new ConstructWorld { Resources = f, CreativeRecycling = false };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (FirstTree(f, map.HalfX) is not { } tree || FirstUnderground(f, map.HalfX, true, "iron vein") is not { } vein)
        {
            Fail("module run: no tree or vein on the map");
            return;
        }
        // ---- The drill.
        var c = ModRig(w, "Drill Module", new Vector3(vein.X, f.Ground(vein.X, vein.Y) - 0.3f, vein.Y), out var sw);
        if (c == null)
            return;
        var n = ConstructRoutes.AllPorts(c, 0, ports);
        var run = -1;
        for (var k = 0; k < n; k++)
            if (ports[k].Name == ConstructGatherer.RunPort)
                run = k;
        Want(run >= 0 && ports[run].Type == RouteType.Data && ports[run].In && !ports[run].Out, "the picker doesn't offer Run");
        Want(c.DriveSeat < 0 && c.Anchored && c.HasBattery && c.Parts[0].Powered, $"the rig: seat {c.DriveSeat}, anchored {c.Anchored}, battery {c.HasBattery}, powered {c.Parts[0].Powered}");
        var st = w.StoreOf(c, 0)!;
        var drillDef = c.DefOf(0);
        Run(1.5f);
        var freeRun = st.Amount;
        var freeDraw = c.PowerUse;
        Want(freeRun > 0 && st.Busy && !st.Held && freeDraw >= drillDef.PowerDraw, $"unwired it runs as before ({freeRun}, busy {st.Busy}, held {st.Held}, {freeDraw} W)");
        Wire(w, c, sw, 0);
        Run(0.5f);
        var held = st.Amount;
        Run(1);
        var heldDraw = c.PowerUse;
        var heldLook = Program.GathererLook(drillDef, st, default);
        Want(st.Held && !st.Busy && st.Amount == held, $"a wire that is off stops it ({st.Amount} from {held}, held {st.Held}, busy {st.Busy})");
        Want(heldDraw < drillDef.PowerDraw, $"held off, it still draws ({heldDraw} W)");
        Want(heldLook.Contains("held off") && heldLook.All(ch => ch is >= ' ' and <= '~'), $"the look line: \"{heldLook}\"");
        c.Parts[sw].State = 1;
        Run(1);
        Want(!st.Held && st.Busy && st.Amount > held && c.PowerUse >= drillDef.PowerDraw, $"a wire that is on runs it ({st.Amount}, held {st.Held}, busy {st.Busy}, {c.PowerUse} W)");
        Want(!Program.GathererLook(drillDef, st, default).Contains("held off"), "the look line still says held off");
        // Cut, mended, deleted.
        var route = 0;
        ConstructRoutes.Damage(w, c, route, 0, 1000);
        Run(0.5f);
        var cutAt = st.Amount;
        Run(0.5f);
        Want(c.Parts[0].InCount == 0 && st.Held && st.Amount == cutAt, $"a cut cable lets it run ({st.Amount}, held {st.Held})");
        ConstructRoutes.Repair(w, c, route);
        Run(0.5f);
        Want(!st.Held && st.Busy, "a mended cable doesn't run it");
        c.Parts[sw].State = 0;
        Run(0.5f);
        Want(st.Held, "off again didn't hold it");
        ConstructRoutes.Remove(w, c, route);
        c.Rebuild();
        Run(0.5f);
        Want(!st.Held && st.Busy, "a deleted link should leave it unwired and running");
        w.Remove(c);

        // ---- The saw.
        var saw = ModRig(w, "Saw Module", tree.At + new Vector3(0, 1.5f, 0), out var ssw);
        if (saw == null)
            return;
        Wire(w, saw, ssw, 0);
        var ss = w.StoreOf(saw, 0)!;
        Run(1);
        Want(ss.Held && ss.Amount == 0 && !ss.Busy, $"a saw held off cut ({ss.Amount})");
        saw.Parts[ssw].State = 1;
        Run(1);
        Want(!ss.Held && ss.Busy && ss.Amount > 0, $"a saw let go didn't cut ({ss.Amount}, busy {ss.Busy})");
        w.Remove(saw);
        map.RestoreProps();

        // ---- The Recycler: nothing in while held off; a job in hand waits.
        yard.RestoreProps();
        World.SetMap(yard);
        var y = new ConstructWorld { CreativeRecycling = false };
        q = World.Query;
        void RunY(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                y.Step(q, World.TickDt);
        }
        if (OpenSpot(y) is not { } spot || BuildBlueprint(y, "Recycling Station", spot.X, spot.Z) is not { } station)
        {
            Fail("module run: no Recycling Station on the yard");
            return;
        }
        station.Owner = 255;
        var ri = RecyclerPartOf(station);
        var rsw = ModFit(y, station, "Switch", "-y", "module run");
        if (rsw < 0)
            return;
        Wire(y, station, rsw, ri);
        var rs = y.StoreOf(station, ri)!;
        var recDef = station.DefOf(ri);
        var wreck = ConstructBuild.PlaceNew(y, ConstructParts.Need("Engine"), IntakeCentre(station, ri), 0)!;
        wreck.Parts[0].Broken = true;
        wreck.Parts[0].Health = 0;
        wreck.Wreck = true;
        wreck.Damaged = true;
        wreck.Rebuild();
        wreck.Pos += IntakeCentre(station, ri) + new Vector3(0, 0.15f, 0) - wreck.WorldCom;
        wreck.Vel = Vector3.Zero;
        var wreckId = wreck.Id;
        RunY(1);
        Want(rs.Held && rs.JobAmount == 0 && y.Get(wreckId) != null, $"a Recycler held off took a wreck in (held {rs.Held}, job {rs.JobAmount})");
        station.Parts[rsw].State = 1;
        RunY(1);
        Want(!rs.Held && rs.JobAmount > 0 && rs.Busy && y.Get(wreckId) == null && station.PowerUse >= recDef.PowerDraw, $"let go it should take the wreck in (job {rs.JobAmount}, {station.PowerUse} W)");
        station.Parts[rsw].State = 0;
        RunY(0.5f);
        var ticks = rs.JobTicks;
        RunY(1);
        Want(rs.Held && rs.JobTicks == ticks && rs.JobAmount > 0, $"a job in hand didn't wait ({rs.JobTicks} from {ticks})");
        Want(station.PowerUse < recDef.PowerDraw, $"a Recycler held off with a job draws {station.PowerUse} W");
        station.Parts[rsw].State = 1;
        RunY(ticks / (float)World.TickRate + 1);
        Want(!rs.Held && rs.JobAmount == 0 && !rs.Busy, $"the job didn't finish (job {rs.JobAmount}, {rs.JobTicks} ticks left)");

        // ---- Blueprints wire it, by a link and by the old wires.
        foreach (var how in (string[])["\"links\": [ { \"type\": \"data\", \"from\": 1, \"to\": 0 } ]", "\"wires\": [ [1, 0] ]"])
        {
            var bp = ConstructBuild.ParseBlueprint($$"""
                { "format": "call-of-slop-construct-blueprint/1", "name": "Wired Drill", "steps": [
                  { "part": "Drill Module" },
                  { "part": "Switch", "on": 0, "socket": "+x", "by": "-x" }
                ], {{how}} }
                """, "wired-drill.json");
            var b = ConstructBuild.Build(y, bp, new Vector3(spot.X + 12, 6, spot.Z), Quaternion.Identity, out var why);
            Want(b != null && b.WireCount == 1 && b.RoutesInto(0, RouteType.Data) == 1 && ConstructGatherer.HeldOff(b, 0), $"a blueprint with {how} ({why}; {b?.WireCount} wires)");
            if (b != null)
                y.Remove(b);
        }
        yard.RestoreProps();
        foreach (var p in problems)
            Fail($"module run: {p}");
    }

    /// <summary>"Modules network": real host, client and a late joiner: the held flag, the look line and a bin's load (mass and centre of mass) reach both copies.</summary>
    private static void CheckModulesNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("modules network: the yard is missing");
            return;
        }
        const ushort port = 26471;
        var keep = sb.Resources;
        var keepInfinite = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        sb.Resources = new SandboxResources { Deposits = [new SandboxDeposit("forest", 0, 0, 24, 1, 0)] };
        sb.InfiniteParts = false;
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            var f = yard.Resources!;
            if (FirstTree(f, yard.HalfX) is not { } tree)
            {
                Fail("modules network: no tree in the yard's forest");
                return;
            }
            if (ModRig(server.Constructs, "Saw Module", tree.At + new Vector3(0, 1.5f, 0), out var sw) is not { } saw)
                return;
            Wire(server.Constructs, saw, sw, 0);
            var st = server.Constructs.StoreOf(saw, 0)!;
            var baseMass = saw.Mass;
            var defSaw = saw.DefOf(0);
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.1)
                    Pump(server, peers, 0.1, ref now, clock);
                return done();
            }
            Construct? Copy(Game g) => g.Constructs.Get(saw.Id);
            CStore? Store(Game g) => Copy(g) is { } cc ? g.Constructs.StoreOf(cc, 0) : null;
            if (!Await(() => Store(a) is { Held: true }, 4, [a]))
                Fail($"modules network: the client never heard the module is held off (server {st.Held}, client {Store(a)?.Held})");
            else if (!Program.GathererLook(defSaw, Store(a)!, default).Contains("held off"))
                Fail("modules network: the client's look line doesn't say held off");
            saw.Parts[sw].State = 1;
            if (!Await(() => Store(a) is { Held: false, Busy: true }, 4, [a]))
                Fail($"modules network: the client never heard it let go (held {Store(a)?.Held}, busy {Store(a)?.Busy}, server amount {st.Amount})");
            Await(() => st.Amount >= 24, 6, [a]);
            saw.Parts[sw].State = 0;
            bool Same(Game g) => Copy(g) is { } cc && Store(g)?.Amount == st.Amount && saw.LoadMass > 0 && MathF.Abs(cc.Mass - saw.Mass) < 0.01f && Vector3.Distance(cc.Com, saw.Com) < 0.001f && !cc.Dirty;
            if (!Await(() => Same(a), 4, [a]))
                Fail($"modules network: the client's mass didn't follow the load ({Copy(a)?.Mass} kg, com {Copy(a)?.Com}; server {saw.Mass}, {saw.Com}; {st.Amount} units)");
            Line($"modules network: {st.Amount} wood: server {saw.Mass:0.0} kg, client {Copy(a)?.Mass:0.0} kg");
            if (saw.Mass <= baseMass)
                Fail($"modules network: the load added no mass ({saw.Mass}, {baseMass})");
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            if (!Await(() => Same(late), 5, [a, late]))
                Fail($"modules network: the joiner's mass isn't the server's ({Copy(late)?.Mass} kg, {saw.Mass} kg)");
            server.Constructs.Withdraw(saw, 0, 100000);
            if (!Await(() => Copy(a) is { } cc && MathF.Abs(cc.Mass - baseMass) < 0.01f && Copy(late) is { } cl && MathF.Abs(cl.Mass - baseMass) < 0.01f, 4, [a, late]))
                Fail($"modules network: the clients' mass didn't come back after emptying ({Copy(a)?.Mass}, {Copy(late)?.Mass}, want {baseMass})");
        }
        finally
        {
            sb.Resources = keep;
            sb.InfiniteParts = keepInfinite;
            yard.DropResources();
            yard.RestoreProps();
        }
    }
}
