using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A drill on a pinned rig with no seat: its engine hangs over it, nose down, and a straight real shaft goes from the engine (or its gearbox) into Drive in.</summary>
    private sealed record DrillRig(Construct C, int Engine, int Switch, int Lever, int From, int Route);

    private const string DdShaftIn = "Drive in", DdChuteOut = "Chute out";

    /// <summary>Fits a part on a socket of the drill by whichever socket of the part goes.</summary>
    private static int DdFit(ConstructWorld w, Construct c, string socket, string part)
    {
        var def = ConstructParts.Need(part);
        var ts = Array.FindIndex(c.DefOf(0).Sockets, s => s.Name == socket);
        for (var s = 0; s < def.Sockets.Length; s++)
            if (ConstructBuild.Place(c, 0, ts, def, s, 0, out var g) == null)
            {
                var i = ConstructBuild.Attach(w, c, def, g);
                c.Rebuild();
                return i;
            }
        return -1;
    }

    private static DrillRig? DdRig(ConstructWorld w, string drill, Vector3 workAt, string engine, bool gearbox, bool clutch, Action<bool, string> want, bool link = true)
    {
        var c = GatherBody(w, drill, workAt);
        c.Pinned = true;
        c.Owner = 255;
        c.Rebuild();
        var d = c.DefOf(0);
        var input = ConstructRoutes.PortNamed(d, RouteType.Shaft, DdShaftIn)!;
        var ed = ConstructParts.Need(engine);
        var drive = ConstructRoutes.PortNamed(ed, RouteType.Shaft, "Drive out")!;
        byte rot = 0;
        for (byte r = 0; r < Rot24.Count; r++)
            if (ConstructParts.DirVector(Rot24.ApplyDir(r, drive.Face)).Y < -0.9f)
            {
                rot = r;
                break;
            }
        var en = w.AddPart(c, ed, ConstructRoutes.PortPoint(c, 0, input.Byte, RouteType.Shaft) + new Vector3(0, 3, 0) - Rot24.Apply(rot, drive.At), rot);
        c.Rebuild();
        FitTurned(w, c, en, "+y", "Fuel Tank", "-y", _ => true);
        var from = en;
        var port = drive.Byte;
        if (gearbox)
        {
            from = RigFit(w, c, from, "+z", "Transmission", "-z");
            port = ConstructRoutes.PortNamed(c.DefOf(from), RouteType.Shaft, "Output shaft")!.Byte;
        }
        if (clutch)
        {
            from = RigFit(w, c, from, "+z", "Clutch", "-z");
            port = ConstructRoutes.PortNamed(c.DefOf(from), RouteType.Shaft, "Output shaft")!.Byte;
        }
        if (!link)
            return new DrillRig(c, en, -1, -1, from, -1); // (the test links it with the linking tool)
        var bad = ConstructRoutes.Check(w, c, RouteType.Shaft, from, port, 255, 0, default, default, 0, input.Byte);
        want(bad == null, $"a straight shaft into the {drill}'s Drive in: {bad}");
        var route = ConstructRoutes.Add(w, c, RouteType.Shaft, from, port, 255, 0, default, default, 0, input.Byte);
        c.Dirty = true;
        c.Rebuild();
        int sw = DdFit(w, c, "back", "Switch"), lever = DdFit(w, c, "-x", "Lever");
        want(route >= 0 && sw >= 0 && lever >= 0 && c.Engine == en, $"the {drill} rig: route {route}, switch {sw}, lever {lever}, engine {c.Engine} of {en} ({c.DriveProblem})");
        if (route < 0 || sw < 0 || lever < 0 || c.Engine != en)
            return null;
        SpWire(w, c, sw, en, 0);
        SpWire(w, c, lever, en, 1);
        return new DrillRig(c, en, sw, lever, from, route);
    }

    private static string DdLook(Construct c, CStore st) => Program.GathererLook(c.DefOf(0), st, new ConstructHit { Body = c, Part = 0 });

    /// <summary>
    /// A Drill Module in the "Gathering" section (docs/sandbox/CONSTRUCTS.md "Gathering"): its ports and data at every tier; a
    /// real engine and a real shaft on an anchored base with no seat at two rpms, the torque taken back, an engine too weak
    /// for it, a gearbox, a clutch, a stopped and a cut shaft with a battery there, no shaft on a battery; the buffer full
    /// with no chute, a chute to a bin, a Storage Volume and a machine keeping it working; the Run wire; all five tiers
    /// through a gearbox into a real chute. The step allocates nothing.
    /// </summary>
    private static void GatherDrive(Map yard, Action<bool, string> want)
    {
        var keep = yard.Sandbox!.Resources;
        yard.Sandbox.Resources = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("iron vein", 0, 0, 34, 1, 0)] };
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            DrillPorts(want);
            var f = yard.Resources!;
            var w = new ConstructWorld { Resources = f, CreativeRecycling = false };
            var q = World.Query;
            Vector3 On(float x, float z) => new(x, f.Ground(x, z) - 0.3f, z);
            int Tapped(float seconds, float x, float z, CStore? empty = null)
            {
                var left = f.LeftAt(x, z);
                var got = 0;
                for (var t = 0; t < seconds * World.TickRate; t++)
                {
                    w.Step(q, World.TickDt);
                    if (empty != null)
                        empty.Amount = 0;
                }
                got = left - f.LeftAt(x, z);
                return got;
            }

            // ---- A real engine, straight onto a real shaft: two speeds.
            const float x0 = -14, z0 = -14;
            if (DdRig(w, "Drill Module", On(x0, z0), "Engine", false, false, want) is not { } rig)
                return;
            var c = rig.C;
            var st = w.StoreOf(c, 0)!;
            var d = c.DefOf(0);
            want(c.DriveSeat < 0 && c.Anchored && c.ShaftFeedCount == 1 && !c.ShaftFeeds![0].Geared && c.HasPowertrain && !c.CanDrive, $"the rig: seat {c.DriveSeat}, anchored {c.Anchored}, {c.ShaftFeedCount} feeds, powertrain {c.HasPowertrain}, drives {c.CanDrive}");
            Tapped(0.5f, x0, z0);
            want(st.Drive == ShaftState.EngineOff && !st.Busy && st.Amount == 0, $"a linked shaft with the engine off: {st.Drive}, busy {st.Busy}, {st.Amount}");
            want(DdLook(c, st).Contains("the engine on its drive shaft isn't running"), $"the look line: \"{DdLook(c, st)}\"");
            c.Parts[rig.Switch].State = 1;
            float Rate(byte lever)
            {
                c.Parts[rig.Lever].State = lever;
                Tapped(1, x0, z0, st);
                return Tapped(1.5f, x0, z0, st) / 1.5f;
            }
            var slow = Rate(0);
            var slowRpm = st.DriveRpm;
            var slowPct = st.DrivePct;
            var slowLoad = c.ShaftLoad;
            var fast = Rate(1);
            Line($"drill drive: an Engine on a shaft: {slow:0.0}/s at {slowRpm} rpm ({slowPct}%), {fast:0.0}/s at {st.DriveRpm} rpm ({st.DrivePct}%), load {c.ShaftLoad:0} N m at the crank");
            want(slow > 0.2f * d.GatherRate && slow < 0.85f * fast && slowRpm < d.GatherRpm && MathF.Abs(slow - d.GatherRate * slowPct / 100f) < 1.5f, $"a slow shaft drills slowly: {slow:0.0}/s at {slowRpm} rpm, {slowPct}%, fast {fast:0.0}/s");
            want(MathF.Abs(fast - d.GatherRate) < 0.8f && st.DrivePct == 100 && st.DriveRpm > d.GatherRpm, $"a fast shaft drills at the tier's rate and no faster: {fast:0.0}/s at {st.DriveRpm} rpm, {st.DrivePct}%");
            want(st.Busy && c.PowerUse == 0 && st.Drive == ShaftState.Turning, $"on the shaft: busy {st.Busy}, {c.PowerUse} W, {st.Drive}");
            want(slowLoad == d.GatherTorque, $"the load direct on the engine is {slowLoad}, want {d.GatherTorque}");
            want(DdLook(c, st).Contains($"drilling, {st.DriveRpm}/1800 rpm") && DdLook(c, st).All(ch => ch is >= ' ' and <= '~'), $"the look line: \"{DdLook(c, st)}\"");

            // ---- The rule: a linked shaft stopped, even with a battery there; cut; mended; the Run wire; deleted, on the battery.
            var battery = DdFit(w, c, "+x", "Battery");
            want(battery >= 0 && c.HasBattery && c.Parts[0].Powered, $"the battery: {battery}, reaches it {c.Parts[0].Powered}");
            c.Parts[rig.Switch].State = 0;
            Tapped(0.5f, x0, z0, st);
            var stopped = Tapped(0.5f, x0, z0, st);
            ConstructRoutes.Add(w, c, RouteType.Power, battery, 255, 255, 0, default, default, rig.Engine, 255); // (the rig's engine hangs in the air: the starter needs the circuit)
            c.Dirty = true;
            want(stopped == 0 && !st.Busy && st.Drive == ShaftState.EngineOff && c.PowerUse < d.PowerDraw && DdLook(c, st).Contains("isn't running"), $"a stopped shaft with a battery: {stopped}, busy {st.Busy}, {st.Drive}, {c.PowerUse} W, \"{DdLook(c, st)}\"");
            c.Parts[rig.Switch].State = 1;
            Tapped(1, x0, z0, st);
            want(st.Busy && st.Drive == ShaftState.Turning && c.PowerUse < d.PowerDraw, $"started on the battery's starter: busy {st.Busy}, {st.Drive}, {c.PowerUse} W, engine {c.EngineRunning}, starter failed {c.StarterFailed}");
            ConstructRoutes.Damage(w, c, rig.Route, 0, 100000);
            c.Dirty = true;
            Tapped(0.3f, x0, z0, st);
            want(!st.Busy && st.Drive == ShaftState.Cut && Tapped(0.5f, x0, z0, st) == 0 && DdLook(c, st).Contains("drive shaft is cut"), $"a cut shaft: busy {st.Busy}, {st.Drive}, \"{DdLook(c, st)}\"");
            ConstructRoutes.Repair(w, c, rig.Route);
            c.Dirty = true;
            Tapped(0.3f, x0, z0, st);
            want(st.Busy && st.Drive == ShaftState.Turning, $"a mended shaft: busy {st.Busy}, {st.Drive}");
            var run = DdFit(w, c, "bottom", "Switch");
            if (run >= 0)
            {
                Wire(w, c, run, 0);
                Tapped(0.3f, x0, z0, st);
                want(st.Held && !st.Busy && c.ShaftLoad == 0 && Tapped(0.5f, x0, z0, st) == 0 && DdLook(c, st).Contains("Run wire is off"), $"Run holds a shaft-driven drill: held {st.Held}, busy {st.Busy}, load {c.ShaftLoad}");
                c.Parts[run].State = 1;
                Tapped(0.3f, x0, z0, st);
                want(!st.Held && st.Busy, $"Run lets it go: held {st.Held}, busy {st.Busy}");
            }
            else
                want(false, "no switch for Run");
            ConstructRoutes.Remove(w, c, rig.Route);
            c.Dirty = true;
            Tapped(0.3f, x0, z0, st);
            var onBattery = Tapped(1, x0, z0, st);
            want(st.Drive == ShaftState.None && st.Busy && c.PowerUse >= d.PowerDraw && MathF.Abs(onBattery - d.GatherRate) <= 2, $"no shaft, on the battery: {st.Drive}, busy {st.Busy}, {c.PowerUse} W, {onBattery}/s");
            want(c.ShaftFeedCount == 0 && !c.HasPowertrain, $"with its shaft gone the engine has no line ({c.ShaftFeedCount}, {c.HasPowertrain})");
            w.Remove(c);

            // ---- Load: an engine too weak on its own, a gear that fixes it; less torque left; more fuel; a clutch.
            if (DdRig(w, "Plasma Drill Module", On(0, -14), "V8 Engine", false, false, want) is { } weak)
            {
                var ws = w.StoreOf(weak.C, 0)!;
                weak.C.Parts[weak.Switch].State = 1;
                weak.C.Parts[weak.Lever].State = 1;
                Tapped(1.5f, 0, -14, ws);
                var rate = Tapped(1, 0, -14, ws);
                var wd = weak.C.DefOf(0);
                Line($"drill drive: a Plasma drill direct on a V8: {rate}/s of {wd.GatherRate} at {ws.DriveRpm} rpm ({ws.DrivePct}%), share {weak.C.ShaftShare:0.00}");
                want(ws.Busy && ws.DrivePct is > 10 and < 100 && ws.DriveRpm >= wd.GatherRpm && rate < 0.95f * wd.GatherRate && weak.C.Rpm < 0.9f * weak.C.DefOf(weak.Engine).MaxRpm * 0.95f, $"a V8 can't carry a Plasma drill direct: {rate}/s, {ws.DrivePct}%, {weak.C.Rpm} rpm");
                want(DdLook(weak.C, ws).Contains("overloaded"), $"the look line: \"{DdLook(weak.C, ws)}\"");
                w.Remove(weak.C);
            }
            if (DdRig(w, "Plasma Drill Module", On(0, -6), "V8 Engine", true, true, want) is { } geared)
            {
                var g = geared.C;
                var gs = w.StoreOf(g, 0)!;
                var gd = g.DefOf(0);
                var clutch = PartNamed(g, "Clutch");
                var clutchSw = DdFit(w, g, "+x", "Switch");
                var hold = DdFit(w, g, "bottom", "Switch");
                if (clutch >= 0 && clutchSw >= 0 && hold >= 0)
                {
                    Wire(w, g, clutchSw, clutch);
                    Wire(w, g, hold, 0);
                    want(g.ShaftFeedCount == 1 && g.ShaftFeeds![0].Geared, $"through a gearbox: {g.ShaftFeedCount} feeds");
                    g.Parts[geared.Switch].State = 1;
                    g.Parts[geared.Lever].State = 1;
                    g.Parts[hold].State = 1;
                    Tapped(1, 0, -6, gs);
                    want(!gs.Busy && gs.Drive == ShaftState.ClutchOut && DdLook(g, gs).Contains("clutch"), $"a clutch out: busy {gs.Busy}, {gs.Drive}, \"{DdLook(g, gs)}\"");
                    g.Parts[clutchSw].State = 1;
                    Tapped(1, 0, -6, gs);
                    var rate = Tapped(1, 0, -6, gs);
                    var torque = g.DriveTorque;
                    var load = g.ShaftLoad;
                    want(gs.DrivePct == 100 && MathF.Abs(rate - gd.GatherRate) <= gd.GatherRate * 0.06f, $"through a gearbox the V8 carries it: {rate}/s of {gd.GatherRate}, {gs.DrivePct}%, {gs.DriveRpm} rpm");
                    want(load > 0 && load < gd.GatherTorque / 2, $"a gear takes the load down: {load} N m from {gd.GatherTorque}");
                    g.Parts[hold].State = 0;
                    Tapped(0.5f, 0, -6, gs);
                    var free = g.DriveTorque;
                    Line($"drill drive: a Plasma drill through a V8's gearbox: {rate}/s, {load:0} N m at the crank, {torque:0} N m left of {free:0}");
                    want(MathF.Abs(free - torque - load) < free * 0.15f, $"working it takes its torque from the engine: {torque} left of {free}, load {load}");
                    g.Parts[geared.Lever].State = 0;
                    g.Parts[hold].State = 1;
                    Tapped(1, 0, -6, gs);
                    var fuel = g.Fuel;
                    Tapped(2, 0, -6, gs);
                    var working = fuel - g.Fuel;
                    g.Parts[hold].State = 0;
                    Tapped(1, 0, -6, gs);
                    fuel = g.Fuel;
                    Tapped(2, 0, -6, gs);
                    var idle = fuel - g.Fuel;
                    Line($"drill drive: fuel in 2 s with the throttle shut: {working:0.000} l drilling, {idle:0.000} l not");
                    want(working > idle * 1.1f, $"drilling burns no more fuel: {working} l against {idle} l");
                }
                else
                    want(false, $"the geared rig's parts: clutch {clutch}, switches {clutchSw}, {hold}");
                w.Remove(g);
            }

            // ---- The buffer: full, it stops and says so; a chute keeps it going, into a bin, a Storage Volume, a machine.
            w.CreativeRecycling = true;
            var lone = GatherBody(w, "Drill Module", On(8, -14));
            var ls = w.StoreOf(lone, 0)!;
            var ld = lone.DefOf(0);
            want(Tapped(3, 8, -14) == ld.BulkCapacity && ls.Amount == ld.BulkCapacity && !ls.Busy, $"a drill with no chute stops at its buffer: {ls.Amount}/{ld.BulkCapacity}, busy {ls.Busy}");
            want(DdLook(lone, ls).Contains("buffer full: connect its Chute out"), $"the look line: \"{DdLook(lone, ls)}\"");
            var bin = DdFit(w, lone, "+x", "Materials Bin");
            var binDef = ConstructParts.Need("Materials Bin");
            var chutePort = ConstructRoutes.PortNamed(ld, RouteType.Chute, DdChuteOut)!;
            var chuteBad = bin < 0 ? "no bin" : ConstructRoutes.Check(w, lone, RouteType.Chute, 0, chutePort.Byte, 255, 0, default, default, bin, ConstructRoutes.FirstChutePort(binDef, input: true));
            want(chuteBad == null, $"a chute from Chute out to a Materials Bin: {chuteBad}");
            var chute = CbChute(w, lone, 0, chutePort.Byte, bin, 255);
            var moved = Tapped(6, 8, -14);
            var bs = w.StoreOf(lone, bin)!;
            want(moved >= 50 && bs.Amount >= 45 && bs.Material == ls.Material, $"a chute to a bin keeps it working: {moved} tapped, {bs.Amount} in the bin");
            ConstructRoutes.Damage(w, lone, chute, 0, 100000);
            lone.Dirty = true;
            Tapped(3, 8, -14);
            want(ls.Amount == ld.BulkCapacity && !ls.Busy && DdLook(lone, ls).Contains("its chute is cut"), $"a cut chute: {ls.Amount}, \"{DdLook(lone, ls)}\"");
            ConstructRoutes.Repair(w, lone, chute);
            lone.Dirty = true;
            w.Withdraw(lone, bin, 0);
            w.Deposit(lone, bin, binDef.BulkCapacity, ls.Material);
            Tapped(1.5f, 8, -14);
            want(!ls.Busy && DdLook(lone, ls).Contains("nowhere to put this"), $"a full bin: \"{DdLook(lone, ls)}\"");
            w.Remove(lone);

            if (CbHull(w, new Vector3(0, 30, 0), new Vector3(6, 3, 4), out var vol) is { } hull)
            {
                var hd = w.AddPart(hull, ConstructParts.Need("Drill Module"), new Vector3(2, 0, 2), 0);
                hull.Rebuild();
                ConstructGatherer.WorkBox(hull, hd, out var bit, out _, out _);
                hull.Pos += On(16, -14) - bit;
                CbChute(w, hull, hd, chutePort.Byte, vol, 255);
                var tapped = Tapped(5, 16, -14);
                var vs = w.StoreOf(hull, vol)!;
                want(tapped >= 40 && vs.Amount >= 35, $"a chute to a Storage Volume keeps it working: {tapped} tapped, {vs.Amount} in the volume");
                w.Remove(hull);
            }
            else
                want(false, "no hull for the Storage Volume");
            var feeder = GatherBody(w, "Drill Module", On(-8, 8));
            var smelter = w.AddPart(feeder, ConstructParts.Need("Smelter"), new Vector3(3, 0, 0), 0);
            feeder.Rebuild();
            CbChute(w, feeder, 0, chutePort.Byte, smelter, 255);
            var fed = Tapped(5, -8, 8);
            var ms = w.StoreOf(feeder, smelter)!;
            want(fed >= 40 && ms.Amount + ms.InAmount > 0, $"a chute to a machine keeps it working: {fed} tapped, smelter {ms.InAmount} in, {ms.Amount} out");
            w.Remove(feeder);

            // ---- All five tiers, in survival, through a gearbox, into a real chute: not throttled by the chute.
            w.CreativeRecycling = false;
            var report = "";
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                float x = -24 + tier * 10, z = 14;
                var name = TierName("Drill Module", tier);
                if (DdRig(w, name, On(x, z), "V8 Engine", true, false, want) is not { } t)
                    continue;
                var tc = t.C;
                var td = tc.DefOf(0);
                var ts = w.StoreOf(tc, 0)!;
                var tb = DdFit(w, tc, "+x", "Materials Bin");
                if (tb < 0)
                {
                    want(false, $"no bin on the {name}");
                    continue;
                }
                var made = CbChute(w, tc, 0, chutePort.Byte, tb, 255);
                want(made >= 0 && ConstructRoutes.Check(w, tc, RouteType.Chute, 0, chutePort.Byte, 255, 0, default, default, DdFit(w, tc, "bottom", "Materials Bin") is var second and >= 0 ? second : tb, 255) is null or "those are already linked", $"the {name}'s Chute out");
                tc.Parts[t.Switch].State = 1;
                tc.Parts[t.Lever].State = 1;
                Tapped(1, x, z);
                w.Withdraw(tc, tb, 100000);
                var tapped = Tapped(2, x, z);
                var binStore = w.StoreOf(tc, tb)!;
                want(MathF.Abs(tapped - 2 * td.GatherRate) <= td.GatherRate * 0.2f && ts.Drive == ShaftState.Turning && ts.DrivePct == 100 && tc.PowerUse == 0, $"the {name} made {tapped} in 2 s at {td.GatherRate}/s ({ts.DrivePct}%, {ts.DriveRpm} rpm, {tc.PowerUse} W)");
                want(ts.Amount < td.BulkCapacity && binStore.Amount >= td.GatherRate, $"the chute keeps up with the {name}: buffer {ts.Amount}/{td.BulkCapacity}, bin {binStore.Amount}");
                report += $"{tapped / 2:0}/s ";
                if (tier == ConstructParts.MaxTier)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var s = 0; s < 60; s++)
                        ConstructGatherer.Step(w);
                    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    want(bytes == 0, $"a shaft-driven drill's step allocates ({bytes} B)");
                }
                w.Remove(tc);
            }
            Line($"drill drive: five tiers on a shaft: {report}");
        }
        finally
        {
            yard.Sandbox.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
        }
    }

    /// <summary>Every tier's ports and shaft data; what a chute may not do; what a bad file is refused for; a Saw Module is left as it was.</summary>
    private static void DrillPorts(Action<bool, string> want)
    {
        var last = 0f;
        for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
        {
            var d = ConstructParts.Need(TierName("Drill Module", tier));
            var chute = ConstructRoutes.PortNamed(d, RouteType.Chute, DdChuteOut);
            var drive = ConstructRoutes.PortNamed(d, RouteType.Shaft, DdShaftIn);
            want(chute is { In: false, Out: true } && drive is { In: true, Out: false } && d.DriveInSocket == drive.Socket, $"{d.Name}'s ports");
            want(ConstructRoutes.IsOutput(d, RouteType.Chute) && !ConstructRoutes.IsInput(d, RouteType.Chute) && !ConstructRoutes.IsWorldInput(d, RouteType.Chute) && ConstructRoutes.IsInput(d, RouteType.Shaft), $"{d.Name}: a chute goes out of it and never in");
            want(d.GatherRpm > 0 && d.GatherTorque > last && ConstructParts.PortProblems(d).Count == 0, $"{d.Name}'s shaft data: {d.GatherRpm} rpm, {d.GatherTorque} N m; {string.Join("; ", ConstructParts.PortProblems(d))}");
            want(d.BulkCapacity >= d.GatherRate * 1.5f && d.BulkCapacity <= d.GatherRate * 2 && d.ChuteLoad == d.GatherRate, $"{d.Name}: a buffer of {d.BulkCapacity} and a chute load of {d.ChuteLoad} at {d.GatherRate}/s");
            last = d.GatherTorque;
            var saw = ConstructParts.Need(TierName("Saw Module", tier));
            want(saw.GatherRpm == 0 && saw.DriveInSocket < 0 && saw.NamedPorts.All(p => p.Type != RouteType.Chute && p.Type != RouteType.Shaft) && saw.BulkCapacity >= 400, $"{saw.Name} changed");
        }
        var file = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Gathering", "DrillModule.json"));
        foreach (var (from, to, why) in ((string, string, string)[])[("\"ratedRpm\": 1800", "\"ratedRpm\": 5", "ratedRpm"), ("\"shaftTorque\": 40", "\"shaftTorque\": 0", "shaftTorque"), (", \"shaftTorque\": 40", "", "shaftTorque"), ("\"type\": \"shaft\"", "\"type\": \"power\", \"at\": [0, 1, 0.68], \"face\": \"+y\", \"fluid2\": 0", "fluid2")])
            try
            {
                ConstructParts.Parse(file.Replace(from, to), "bad.json");
                want(false, $"a drill with {to} wasn't refused");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(why), $"a drill with {to}: \"{ex.Message}\"");
            }
        var w = new ConstructWorld();
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Drill Module"), new Vector3(0, 30, 0), 0) is not { } c)
        {
            want(false, "no drill to link");
            return;
        }
        var bin = DdFit(w, c, "+x", "Materials Bin");
        var into = ConstructRoutes.Check(w, c, RouteType.Chute, bin, 255, 255, 0, default, default, 0, 255);
        want(into != null && into.Contains("no chute input port"), $"a chute into a drill: \"{into}\"");
        want(!ConstructRoutes.ClickEnds(c, RouteType.Chute, bin, 255, 0, false), "a click on a drill ends a chute");
        Span<ConstructRoutes.LinkPort> ports = new ConstructRoutes.LinkPort[ConstructRoutes.MaxLinkPorts];
        var n = ConstructRoutes.AllPorts(c, 0, ports);
        var names = string.Join(", ", ports[..n].ToArray().Select(p => p.Name));
        want(names.Contains(DdChuteOut) && names.Contains(DdShaftIn) && names.Contains(ConstructGatherer.RunPort) && names.Contains("Power in") && !names.Contains("Chute,"), $"the linking tool's picker on a drill: {names}");
        w.Remove(c);
    }

    /// <summary>"Gathering network": a real host, client and late joiner hear how a drill's shaft stands, and a client's look line says what the host's does: stopped, full, working; a chute's load reaches the bin on both.</summary>
    private static void GatherDriveNet(Game server, Game a, Game late, ResourceField f, ushort port, ref double now, Stopwatch clock)
    {
        var w = server.Constructs;
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        Vector3 On(float x, float z) => new(x, f.Ground(x, z) - 0.3f, z);
        if (DdRig(w, "Drill Module", On(8, 2), "Engine", false, false, Want) is not { } rig)
        {
            Fail($"gathering network: no shaft rig: {string.Join("; ", problems)}");
            return;
        }
        var c = rig.C;
        var chutes = GatherBody(w, "Workshop Drill Module", On(8, 8));
        var bin = DdFit(w, chutes, "+x", "Materials Bin");
        var outByte = ConstructRoutes.PortNamed(chutes.DefOf(0), RouteType.Chute, DdChuteOut)!.Byte;
        CbChute(w, chutes, 0, outByte, bin, 255);
        var creative = w.CreativeRecycling;
        w.CreativeRecycling = true;
        var st = w.StoreOf(c, 0)!;
        CStore? Store(Game g, Construct b, int part) => g.Constructs.Get(b.Id) is { } cc ? g.Constructs.StoreOf(cc, part) : null;
        string Look(Game g) => g.Constructs.Get(c.Id) is { } cc && Store(g, c, 0) is { } s ? DdLook(cc, s) : "(no copy)";
        var pumped = now;
        bool Await(Func<bool> done, double seconds, Game[] peers)
        {
            for (var t = 0.0; t < seconds && !done(); t += 0.1)
                Pump(server, peers, 0.1, ref pumped, clock);
            return done();
        }
        Game[] both = [a, late];
        Want(Await(() => Store(a, c, 0) is { Drive: ShaftState.EngineOff } && Store(late, c, 0) is { Drive: ShaftState.EngineOff }, 4, both), $"the clients never heard a stopped shaft ({Store(a, c, 0)?.Drive}, {Store(late, c, 0)?.Drive}, host {st.Drive})");
        Want(Look(a).Contains("isn't running") && Look(late).Contains("isn't running"), $"a client's look line: \"{Look(a)}\"");
        c.Parts[rig.Switch].State = 1;
        c.Parts[rig.Lever].State = 1;
        Want(Await(() => Store(a, c, 0) is { Drive: ShaftState.Turning, Busy: true, DriveRpm: >= 1800 }, 4, both), $"the client never heard the shaft turning ({Store(a, c, 0)?.Drive}, busy {Store(a, c, 0)?.Busy}, {Store(a, c, 0)?.DriveRpm} rpm)");
        Line($"gathering network: shaft at {st.DriveRpm} rpm on the host, {Store(a, c, 0)?.DriveRpm} on the client: \"{Look(a)}\"");
        Want(Await(() => Store(a, c, 0) is { Busy: false, Amount: >= 20 } && Store(late, c, 0) is { Busy: false, Amount: >= 20 }, 5, both), $"the clients never heard the buffer fill ({Store(a, c, 0)?.Amount}, {Store(late, c, 0)?.Amount}, host {st.Amount})");
        Want(Look(a).Contains("buffer full: connect its Chute out"), $"a full buffer on a client: \"{Look(a)}\"");
        Want(Await(() => Store(a, chutes, bin) is { Amount: >= 40 } && Store(late, chutes, bin) is { Amount: >= 40 }, 6, both), $"the chute's load never reached the clients' bin ({Store(a, chutes, bin)?.Amount}, {Store(late, chutes, bin)?.Amount}, host {w.StoreOf(chutes, bin)?.Amount})");
        w.Withdraw(c, 0, 100);
        using var joiner = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Joiner", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] all = [a, late, joiner];
        Want(Await(() => Store(joiner, c, 0) is { Drive: ShaftState.Turning, DrivePct: 100 }, 6, all), $"a joiner never heard the shaft turning ({Store(joiner, c, 0)?.Drive}, {Store(joiner, c, 0)?.DrivePct}%)");
        c.Parts[rig.Switch].State = 0;
        Want(Await(() => Store(joiner, c, 0) is { Drive: ShaftState.EngineOff } && Store(a, c, 0) is { Drive: ShaftState.EngineOff }, 4, all), $"a joiner never heard it stop ({Store(joiner, c, 0)?.Drive})");
        w.CreativeRecycling = creative;
        w.Remove(c);
        w.Remove(chutes);
        now = pumped;
        foreach (var p in problems)
            Fail($"gathering network: {p}");
    }
}
