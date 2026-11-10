using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

// PROBE-TMP: a throwaway walker probe (removed before the branch finishes).
public static partial class SelfTest
{
    public static int WalkerProbe(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var mapName = Flag("--map") ?? "Proving Ground";
        var file = Flag("--bp") ?? Path.Combine(VehicleKit.VehicleRegistry.RepoBlueprints(), "TitanWalker.json");
        var wreck = int.TryParse(Flag("--wreck"), out var wr) ? wr : 0;
        var phase = float.TryParse(Flag("--phase"), System.Globalization.CultureInfo.InvariantCulture, out var ph) ? ph : 6f;
        var settle = float.TryParse(Flag("--settle"), System.Globalization.CultureInfo.InvariantCulture, out var st) ? st : 8f;
        var client = args.Contains("--client");
        var verbose = args.Contains("--verbose");
        var only = Flag("--only");
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
        // The flattest spot: ground spread over the walker's footprint.
        var best = Vector3.Zero;
        var bestSpread = float.MaxValue;
        var q = World.Query;
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
        if (Flag("--at") is { } at)
        {
            var xs = at.Split(',');
            best = ConstructWorld.Ground(q, float.Parse(xs[0]), float.Parse(xs[1]));
        }
        Console.WriteLine($"map {map.Name}: spot {best} (spread {bestSpread:0.0} m)");
        var c = ConstructBuild.BuildOnGround(w, bp, best, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"doesn't build: {why}");
            return 1;
        }
        c.Wake();
        var id = server.LocalId;
        var helm = c.DriveSeat;
        Console.WriteLine($"built {c.PartCount} parts, {c.Mass / 1000:0} t, legs {c.LegCount}, drive seat {helm}, host id {id}, charge {c.Charge:0} kJ / {c.ChargeCapacity:0}");
        if (helm < 0)
            return 1;
        var me = server.Local!;
        server.SelfTestRespawn(me, now);
        me.ImmuneUntil = 1e9;
        PumpFast(server, clients, 0.2, ref now);
        me.Teleport(ConstructEdits.SeatFeet(c, helm) + new Vector3(0, 0.1f, 0));
        var sat = server.SandboxHands.Use(w, id, me.Eye, c.Id, helm);
        Console.WriteLine($"sit: {sat ?? "ok"}; driver {c.Driver}, alive {me.Alive}");
        for (var k = 0; k < wreck; k++)
        {
            var leg = c.Legs[k == 0 ? 2 : 4].Part; // (a middle leg, then a front one)
            if (k >= 2)
                leg = c.Legs[k].Part;
            w.DamagePart(c, leg, 1e12f, out _, raw: true);
        }
        var p0 = c.Pos;
        var t0 = 0;
        void Report(string what)
        {
            var d = c.Pos - p0;
            var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
            var yawDeg = MathF.Atan2(fwd.X, -fwd.Z) * 180 / MathF.PI;
            int hold = 0, gr = 0, sw = 0;
            float worst = 0;
            for (var k = 0; k < c.LegCount; k++)
            {
                hold += c.Legs[k].Hold ? 1 : 0;
                gr += c.Legs[k].Grounded ? 1 : 0;
                sw += c.Legs[k].Swinging ? 1 : 0;
                worst = MathF.Max(worst, Vector3.Distance(c.ToWorld(c.Legs[k].Hip), c.Legs[k].Foot) / c.Legs[k].Reach);
            }
            Console.WriteLine($"  {what,-10} t {t0 / 60f,5:0.0} moved ({d.X,6:0.0},{d.Y,5:0.0},{d.Z,6:0.0}) v {new Vector2(c.Vel.X, c.Vel.Z).Length():0.00} vy {c.Vel.Y:0.00} yaw {yawDeg:0} top {c.WalkTop:0.00} plift {c.WalkPowerLift:0.000} thr {c.Throttle} st {c.SteerInput} roll {c.RollInput} lift {c.LiftInput} drv {c.Driver} walk {c.Walking} step {c.WalkStepping} wait {c.WalkWait} share {ConstructPhysics.DbgShare:0.00} edge {ConstructPhysics.DbgEdge} tgt {ConstructPhysics.DbgTarget:0.00} vyT {ConstructPhysics.DbgVy:0.00} wh {c.WalkHeight:0.0} shift {ConstructPhysics.DbgShift.Length():0.00} hold {hold} gr {gr} sw {sw} stretch {worst:0.00} chg {c.Charge / 1e6:0.0} GJ prob '{c.WalkProblem}' asleep {c.Asleep}");
        }
        (string, Buttons, int)[] phases =
        [
            ("settle", Buttons.None, 0), ("W", Buttons.Forward, 0), ("S", Buttons.Back, 0), ("D", Buttons.Right, 0), ("A", Buttons.Left, 0),
            ("strafeR", Buttons.None, 16), ("strafeL", Buttons.None, 8), ("raise", Buttons.Sprint, 0), ("lower", Buttons.Crouch, 0), ("W2", Buttons.Forward, 0),
        ];
        var worstMs = 0.0;
        foreach (var (name, b, keys) in phases)
        {
            if (only != null && name != "settle" && !only.Split(',').Contains(name))
                continue;
            var secs = name == "settle" ? settle : phase;
            server.SandboxSend(CActKind.SeatKeys, part: (byte)keys);
            p0 = c.Pos;
            var start = c.Pos;
            var startYaw = Vector3.Transform(c.SeatForward(), c.Rot);
            var ticks = (int)(secs * 60);
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
                t0 += 3;
                if (verbose && t % 30 == 0 || t + 3 >= ticks)
                {
                    Report(name);
                    if (args.Contains("--contacts"))
                        Console.WriteLine("    " + ConstructPhysics.DbgContacts(c));
                }
            }
            var moved = c.Pos - start;
            var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
            var turned = MathF.Atan2(Vector3.Cross(startYaw, fwd).Y, Vector3.Dot(startYaw, fwd)) * 180 / MathF.PI;
            var along = Vector3.Dot(moved, Vector3.Normalize(fwd with { Y = 0 }));
            var side = Vector3.Dot(moved, Vector3.Normalize(Vector3.Cross(fwd with { Y = 0 }, Vector3.UnitY)));
            Console.WriteLine($"{name}: along {along:0.0} m ({along / secs:0.00} m/s), right {side:0.0} m, up {moved.Y:0.0} m, turned {turned:0.0} deg");
            if (client && clients[0].Constructs.Get(c.Id) is { } cc)
                Console.WriteLine($"   client copy: pos off {Vector3.Distance(cc.Pos, c.Pos):0.0} m, problem '{cc.WalkProblem}', top {cc.WalkTop:0.00}, feet set {cc.NetFeetSet}");
        }
        Console.WriteLine($"worst update/3 {worstMs:0.0} ms");
        foreach (var g in clients)
            g.Dispose();
        return 0;
    }
}
