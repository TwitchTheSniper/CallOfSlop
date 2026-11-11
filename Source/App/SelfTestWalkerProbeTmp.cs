using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

// PROBE-TMP: a throwaway walker speed probe (removed before the branch finishes).
public static partial class SelfTest
{
    public static int WalkerProbe(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        float FlagF(string f, float d) => float.TryParse(Flag(f), CultureInfo.InvariantCulture, out var v) ? v : d;
        var mapName = Flag("--map") ?? "Proving Ground";
        var file = Flag("--bp") ?? Path.Combine(VehicleKit.VehicleRegistry.RepoBlueprints(), "TitanWalker.json");
        var wreck = (int)FlagF("--wreck", 0);
        var client = args.Contains("--client");
        var verbose = args.Contains("--verbose");
        var plat = Flag("--platform"); // a part name: a frozen slab under the walker (cliff: its edge; ramp: tilted by --tilt)
        var platUp = FlagF("--up", 20);
        var tilt = FlagF("--tilt", 0);
        // phases: name:keys:seconds:speed% , e.g. W:W:6:100
        var script = Flag("--script") ?? "settle:-:4:100,W100:W:6:100,W200:W:6:200,W50:W:6:50,coast:-:3:200";
        if (Maps.Find(mapName) is not { } map)
        {
            Console.WriteLine($"no map {mapName}");
            return 1;
        }
        map.RestoreProps();
        World.SetMap(map);
        const ushort port = 26397;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        var clients = new List<Game>();
        if (client)
            clients.Add(new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id));
        Pump(server, clients, 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, clients, 0.25, ref now);
        Pump(server, clients, 0.3, ref now, clock);
        if (client)
        {
            server.Net.SimulateBadNetwork(60, 10, 2);
            clients[0].Net.SimulateBadNetwork(60, 10, 2);
        }
        var w = server.Constructs;
        foreach (var body in w.Bodies.ToList())
            w.Remove(body);
        var bp = ConstructBuild.ParseBlueprint(File.ReadAllText(file), file);
        var q = World.Query;
        Vector3 best;
        if (Flag("--at") is { } at)
        {
            var xs = at.Split(',');
            best = ConstructWorld.Ground(q, float.Parse(xs[0], CultureInfo.InvariantCulture), float.Parse(xs[1], CultureInfo.InvariantCulture));
        }
        else
        {
            best = Vector3.Zero;
            var bestSpread = float.MaxValue;
            for (var x = -800f; x <= 800f; x += 40f)
            for (var z = -800f; z <= 800f; z += 40f)
            {
                if (!World.Map.InsideBounds(x, z, 150))
                    continue;
                float lo = float.MaxValue, hi = float.MinValue;
                for (var dx = -100f; dx <= 100f; dx += 20f)
                for (var dz = -80f; dz <= 80f; dz += 20f)
                {
                    var y = ConstructWorld.Ground(q, x + dx, z + dz).Y;
                    lo = MathF.Min(lo, y);
                    hi = MathF.Max(hi, y);
                }
                var spread = hi - lo + 0.002f * MathF.Sqrt(x * x + z * z);
                if (spread < bestSpread)
                {
                    bestSpread = spread;
                    best = ConstructWorld.Ground(q, x, z);
                }
            }
        }
        Construct? slab = null;
        var spawn = best;
        if (plat != null)
        {
            var pb = ConstructBuild.ParseBlueprint($"{{\"format\":\"call-of-slop-construct-blueprint/1\",\"name\":\"slab\",\"steps\":[{{\"part\":\"{plat}\"}}]}}", "slab");
            slab = ConstructBuild.BuildOnGround(w, pb, best, 0, out var sw);
            if (slab == null)
            {
                Console.WriteLine($"slab: {sw}");
                return 1;
            }
            slab.Pos += Vector3.UnitY * platUp;
            slab.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, tilt * MathF.PI / 180);
            slab.PrevPos = slab.NetPos = slab.NetFromPos = slab.Pos;
            slab.Frozen = true;
            slab.Vel = slab.AngVel = Vector3.Zero;
            slab.NetHistReset();
            slab.Rebuild();
            var top = slab.ToWorld(new Vector3(0, slab.LocalMax.Y, 0));
            var offZ = FlagF("--onz", 0); // spawn this far along the slab's z (its edge is at LocalMax.Z / Min.Z)
            spawn = slab.ToWorld(new Vector3(0, slab.LocalMax.Y + 0.05f, offZ));
            Console.WriteLine($"slab {plat}: {slab.LocalMin} .. {slab.LocalMax}, top at {top}, spawn {spawn}");
        }
        var c = ConstructBuild.BuildOnGround(w, bp, spawn, FlagF("--yaw", 0), out var why);
        if (c == null)
        {
            Console.WriteLine($"doesn't build: {why}");
            return 1;
        }
        if (slab != null)
        {
            // (BuildOnGround found the map's ground: put the walker standing on the slab)
            var low = float.MaxValue;
            for (var k = 0; k < c.LegCount; k++)
                low = MathF.Min(low, c.ToWorld(c.Legs[k].Rest).Y);
            Console.WriteLine($"walker placed at {c.Pos}, lowest rest foot {low}, legs {c.LegCount}");
            c.Pos += Vector3.UnitY * (spawn.Y - low + 0.05f);
            c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
            c.FeetSet = false;
            c.NetHistReset();
        }
        c.Wake();
        if (Flag("--stride") is { } strideTxt)
            Console.WriteLine("(stride flag ignored)");
        var id = server.LocalId;
        var helm = c.DriveSeat;
        var fwd0 = Vector3.Transform(c.SeatForward(), c.Rot);
        Console.WriteLine($"built {c.PartCount} parts, {c.Mass:0} kg, legs {c.LegCount}, drive seat {helm}, charge {c.Charge:0} kJ / {c.ChargeCapacity:0}, plant {c.PlantOut:0} kW, seat fwd {fwd0}");
        if (helm < 0)
            return 1;
        var me = server.Local!;
        server.SelfTestRespawn(me, now);
        me.ImmuneUntil = 1e9;
        PumpFast(server, clients, 0.2, ref now);
        me.Teleport(ConstructEdits.SeatFeet(c, helm) + new Vector3(0, 0.1f, 0));
        var sat = server.SandboxHands.Use(w, id, me.Eye, c.Id, helm);
        Console.WriteLine($"sit: {sat ?? "ok"}; driver {c.Driver}");
        void SetSpeed(float pct)
        {
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var p = ref c.Parts[c.Legs[k].Part];
                var slot = p.D.CfgSlot[(int)CfgKey.WalkSpeed];
                if (slot >= 0)
                    ConstructConfig.Set(ref p, p.D, slot, pct);
            }
        }
        var worstMs = 0.0;
        var t0 = 0;
        var maxStretch = 0f;
        var landed = 0;
        foreach (var ph in script.Split(','))
        {
            var f = ph.Split(':');
            var name = f[0];
            var b = f[1] switch { "W" => Buttons.Forward, "S" => Buttons.Back, "D" => Buttons.Right, "WD" => Buttons.Forward | Buttons.Right, "A" => Buttons.Left, _ => Buttons.None };
            var secs = float.Parse(f[2], CultureInfo.InvariantCulture);
            SetSpeed(float.Parse(f[3], CultureInfo.InvariantCulture));
            if (name == "wreck")
            {
                w.DamagePart(c, c.Legs[2].Part, 1e12f, out _, raw: true);
                Console.WriteLine("wrecked leg 2");
                void Dump(string when)
                {
                    Console.WriteLine($"  {when}: legs {c.LegCount} groups {c.LegGroups} live {c.WalkLive} feetSet {c.FeetSet} pos {c.Pos}");
                    for (var k = 0; k < c.LegCount; k++)
                        Console.WriteLine($"    leg {k} part {c.Legs[k].Part} live {ConstructPhysics.LegLive(c, k)} grp {c.Legs[k].Group} sw {c.Legs[k].Swinging} gr {c.Legs[k].Grounded} hold {c.Legs[k].Hold} foot {c.Legs[k].Foot} to {c.Legs[k].To} hip {c.ToWorld(c.Legs[k].Hip)}");
                }
                Dump("before");
                now += 0.05;
                server.Update(now, 0.05);
                Dump("after");
            }
            var start = c.Pos;
            var startYaw = Vector3.Transform(c.SeatForward(), c.Rot);
            var ticks = (int)(secs * 60);
            float vmax = 0, vsum = 0;
            var vn = 0;
            maxStretch = 0;
            landed = 0;
            double stopAt = -1;
            var stopFrom = Vector3.Zero;
            for (var t = 0; t < ticks; t += 3)
            {
                server.HeldButtons = b;
                var sw = Stopwatch.StartNew();
                if (client)
                {
                    var tt = clock.Elapsed.TotalSeconds;
                    while (clock.Elapsed.TotalSeconds - tt < 0.05)
                        Thread.Sleep(1);
                    now += 0.05;
                    server.Update(now, 0.05);
                    foreach (var g in clients)
                        g.Update(now, 0.05);
                }
                else
                {
                    now += 0.05;
                    server.Update(now, 0.05);
                }
                worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds / 3);
                if (sw.Elapsed.TotalMilliseconds / 3 > 8)
                    Console.WriteLine($"    SLOW t {t / 60f:0.00} {sw.Elapsed.TotalMilliseconds / 3:0.0} ms/tick at {c.Pos.X:0.0},{c.Pos.Z:0.0} v {c.Vel.Length():0.0}");
                t0 += 3;
                var v = new Vector2(c.Vel.X, c.Vel.Z).Length();
                vmax = MathF.Max(vmax, v);
                if (t > ticks / 2)
                {
                    vsum += v;
                    vn++;
                }
                if (c.WalkLanded)
                    landed++;
                for (var k = 0; k < c.LegCount; k++)
                    if (!c.Legs[k].Swinging && c.Legs[k].Grounded && ConstructPhysics.LegLive(c, k))
                        maxStretch = MathF.Max(maxStretch, Vector3.Distance(c.ToWorld(c.Legs[k].Hip), c.Legs[k].Foot) / c.Legs[k].Reach);
                if (b == Buttons.None && stopAt < 0 && v < 0.05f)
                {
                    stopAt = t / 60.0;
                    stopFrom = c.Pos - start;
                }
                if (args.Contains("--legs") && (t / 60f) > FlagF("--from", 0) && (t / 60f) < FlagF("--to", 99))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"    t {t / 60f:0.00} v {v:0.00} yawr {c.AngVel.Y:0.00} st {c.SteerInput} rad {c.Radius:0.0} wait {c.WalkWait} step {c.WalkStepping} g {c.WalkGroup} clk {c.WalkClock:0.00} |");
                    for (var k = 0; k < c.LegCount; k++)
                    {
                        var l = c.ToLocal(c.Legs[k].Foot) - ConstructPhysics.LegRest(c, k);
                        sb.Append($" {c.Legs[k].Group}{(c.Legs[k].Swinging ? "S" : c.Legs[k].Grounded ? "G" : "-")}{(c.Legs[k].Hold ? "" : "x")} {l.Z:0.00}/{Vector3.Distance(c.ToWorld(c.Legs[k].Hip), c.Legs[k].Foot) / c.Legs[k].Reach:0.00}");
                    }
                    Console.WriteLine(sb.ToString());
                }
                if (verbose && t % 30 == 0)
                    Console.WriteLine($"    t {t / 60f:0.0} pos {c.Pos.X:0.0},{c.Pos.Y:0.0},{c.Pos.Z:0.0} v {v:0.00} vy {c.Vel.Y:0.00} wh {c.WalkHeight:0.0} top {c.WalkTop:0.00} base {c.WalkBase:0.00} swing {c.WalkSwing:0.000} edge {c.WalkEdgeCap:0.0} walk {c.Walking} prob '{c.WalkProblem}' groups {c.LegGroups} held {c.WalkPowerHeld} W {c.LegWatts / 1e6:0.000} MW chg {c.Charge:0}");
            }
            var moved = c.Pos - start;
            var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
            var turned = MathF.Atan2(Vector3.Cross(startYaw, fwd).Y, Vector3.Dot(startYaw, fwd)) * 180 / MathF.PI;
            var along = Vector3.Dot(moved, Vector3.Normalize(startYaw with { Y = 0 }));
            Console.WriteLine($"{name}: along {along:0.0} m, up {moved.Y:0.0}, turned {turned:0.0} deg, v late {(vn > 0 ? vsum / vn : 0):0.00} max {vmax:0.00} m/s, top {c.WalkTop:0.00} (base {c.WalkBase:0.00}, set {c.WalkSpeed * 100:0}%, held {c.WalkPowerHeld}), swing {c.WalkSwing:0.000} s, groups {c.LegGroups}, stretch max {maxStretch:0.00}, landings {landed}, legW {c.LegWatts / 1e3:0.0} kW, plift {c.WalkPowerLift:0.000}, chg {c.Charge:0}/{c.ChargeCapacity:0} kJ, walking {c.Walking} '{c.WalkProblem}'"
                + (stopAt >= 0 ? $", stopped after {stopAt:0.00} s, {new Vector2(stopFrom.X, stopFrom.Z).Length():0.00} m" : "")
                + (slab != null ? $", on slab {slab.ToLocal(c.WorldCom)} (box {slab.LocalMin}..{slab.LocalMax}), y {c.WorldCom.Y - slab.Pos.Y:0.0}" : ""));
            if (args.Contains("--look"))
            {
                var fw = Vector3.Normalize(Vector3.Transform(c.SeatForward(), c.Rot) with { Y = 0 });
                for (var hgt = -2f; hgt <= 2f; hgt += 1f)
                {
                    var o = c.WorldCom + Vector3.UnitY * hgt * c.Legs[0].Reach * 0.4f;
                    Console.WriteLine($"   look h {hgt}: ray {World.RaycastWorld(o, fw, 60):0.0} m, ground under +5 m {ConstructWorld.Ground(q, (o + fw * 5).X, (o + fw * 5).Z).Y - c.WorldCom.Y:0.0}, com {c.WorldCom}");
                }
            }
            if (client && clients[0].Constructs.Get(c.Id) is { } cc)
            {
                var off = 0f;
                for (var k = 0; k < cc.LegCount && k < c.LegCount; k++)
                    off = MathF.Max(off, Vector3.Distance(cc.Legs[k].Foot, c.Legs[k].Foot));
                Console.WriteLine($"   client copy: pos off {Vector3.Distance(cc.Pos, c.Pos):0.00} m, feet off at most {off:0.00} m, problem '{cc.WalkProblem}', top {cc.WalkTop:0.00}, held {cc.WalkPowerHeld}, feet set {cc.NetFeetSet}");
            }
        }
        Console.WriteLine($"worst update/3 {worstMs:0.0} ms");
        foreach (var g in clients)
            g.Dispose();
        return 0;
    }
}
