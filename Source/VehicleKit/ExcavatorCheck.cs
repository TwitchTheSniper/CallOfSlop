using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// <c>--excavator-check [--seed N] [--map NAME] [--no-client]</c> (docs/vehiclekit/EXCAVATOR-notes.md): a Militia Excavator on a real host's Sandbox world with terrain
/// (the Construct Yard; <c>--map Planetoid</c> for a round world), and a joined client. Worked through its own levers and its seat's slew keys: the boom down, the
/// bucket dug into the ground, curled, the house slewed round, a Militia Ore Hauler set under the bucket and the bucket tipped into its bed; then ore through the
/// drill's chute into the bucket and poured again; on a round world the drill is lowered onto a vein. Each step says what moved, how much, and what the client saw.
/// Exit code: failures. About a minute of game time; nothing is saved.
/// </summary>
public static class ExcavatorCheck
{
    public static int Cli(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var seed = int.TryParse(Flag("--seed"), out var s) ? s : 1;
        var mapName = Flag("--map") ?? "Construct Yard";
        var withClient = !args.Any(a => a.Equals("--no-client", StringComparison.OrdinalIgnoreCase));
        var fails = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            fails += ok ? 0 : 1;
        }
        if (Maps.Find(mapName) is not { } map || map.Terrain == null && map.Planetoid == null)
        {
            Console.WriteLine($"excavator check: no map \"{mapName}\" with terrain or a planetoid");
            return 1;
        }
        var gen = VehicleRegistry.Find("Militia Excavator")!;
        var text = VehicleRegistry.Make(gen, seed).Text();
        var haulerText = VehicleRegistry.Make(VehicleRegistry.Find("Militia Ore Hauler")!, 1).Text();
        map.RestoreProps();
        World.SetMap(map);
        const ushort port = 26411; // (outside the self-tests' 26123-26311: SelfTest.cs)
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var client = withClient ? new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Watcher", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id) : null;
        var now = 0.0;
        var clock = Stopwatch.StartNew();
        Construct? body = null;
        var seatPart = -1;
        var roll = 0;
        void Tick(float seconds)
        {
            var n = (int)(seconds * World.TickRate);
            for (var k = 0; k < n; k++)
            {
                if (server.Local is { } sitter)
                    server.SandboxHands.Hand(sitter.Id).SeatKeys = (ushort)(roll == 0 ? 0 : roll > 0 ? ConstructParts.KeyRollRight : ConstructParts.KeyRollLeft); // (E or Q held in the seat, as the client's SeatKeys message sets it)
                now += World.TickDt;
                server.Update(now, World.TickDt);
                client?.Update(now, World.TickDt);
                if (k % 3 == 0)
                    Thread.Sleep(1); // (the loopback's packets)
            }
        }
        for (var wait = 0; wait < 80 && server.Phase != MatchPhase.Playing; wait++)
            Tick(0.25f);
        Tick(1);
        Say(server.TerrainDigLive, $"the host's world digs ({map.Name}, Sandbox, live {server.TerrainDigLive}); client joined: {(client == null ? "not asked" : client.LocalId != byte.MaxValue ? "yes" : "NO")}");
        if (server.Local is { } host)
        {
            host.ImmuneUntil = now + 1e6;
            server.RequestRespawn();
        }
        for (var t = 0; t < 60 && server.Local is not { Alive: true }; t++)
            Tick(0.25f);
        if (server.Local is not { Alive: true } me)
        {
            Console.WriteLine("excavator check: the host's player never spawned");
            return fails + 1;
        }
        // Where to stand it: open ground; on a round world, over the nearest vein a tier-1 drill can work.
        var pl = map.Planetoid;
        var groundOnly = new QueryFilter(QueryLayers.StaticShapes | QueryLayers.Terrain); // (the ground and the map's solids, never a construct: the arm itself is over it)
        Vector3 Ground(Vector3 p)
        {
            var u = UpAt(p);
            var from = p + u * 6;
            var d = World.Query.Raycast(from, -u, 120, groundOnly);
            return float.IsFinite(d) && d < 120 ? from - u * d : pl != null ? pl.GroundUnder(from) : p with { Y = map.Terrain!.Sample(p.X, p.Z) };
        }
        Vector3 UpAt(Vector3 p) => pl != null ? Vector3.Normalize(p - pl.Center) : Vector3.UnitY;
        Vector3 spot;
        if (pl != null)
            spot = VeinSpot(map, pl) ?? Ground(pl.Center + Vector3.Normalize(Map.SurfaceDir(0, 90)) * (float)(pl.Radius + 50));
        else
        {
            // Open ground away from the yard's sentries and pieces (a sentry shoots a sitter: its rounds broke the arm off once): the candidate furthest from every body.
            var bodies = server.Constructs.Bodies.Where(b => b.GunCount > 0).Select(b => b.WorldCom).ToList();
            var at = Vector3.Zero;
            var far = -1f;
            var car = server.Constructs.Bodies.FirstOrDefault(b => b.CanDrive)?.WorldCom ?? Vector3.Zero;
            for (var x = (int)car.X - 30; x <= car.X + 30; x += 6)
                for (var z = (int)car.Z - 30; z <= car.Z + 30; z += 6)
                {
                    var p = new Vector3(x, 30, z);
                    var near = bodies.Count == 0 ? 1e6f : bodies.Min(b => Vector2.Distance(new Vector2(b.X, b.Z), new Vector2(p.X, p.Z)));
                    float H(float dx, float dz) => Ground(new Vector3(x + dx, 30, z + dz)).Y;
                    if (near > far && MathF.Abs(H(6, 0) - H(-6, 0)) < 0.6f && MathF.Abs(H(0, 6) - H(0, -6)) < 0.6f && MathF.Abs(H(0, 0) - H(9, 0)) < 0.6f)
                        (far, at) = (near, p);
                }
            spot = Ground(at);
            Console.WriteLine($"  the yard's first car at {car.X:0} {car.Y:0} {car.Z:0}");
            Console.WriteLine($"  spot {at.X:0} {at.Z:0}: {far:0} m from the nearest of {bodies.Count} armed bodies");
        }
        var bp = ConstructBuild.ParseBlueprint(text, "excavator.json");
        body = server.SandboxVehicleBuild(255, spot, 0, bp, null, null, out var why);
        if (body == null)
        {
            Console.WriteLine($"excavator check: it doesn't build there: {why}");
            return fails + 1;
        }
        var c = body;
        int Part(string name, int nth = 0) => Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == name).Skip(nth).DefaultIfEmpty(-1).First();
        int Joint(int part) => Enumerable.Range(0, c.JointCount).FirstOrDefault(j => c.Joints[j].Hinge == part, -1);
        seatPart = Part("Seat");
        var boom = Part("Motor Hinge 3x3 Medium");
        var stick = Part("Motor Hinge 3x3 Medium", 1);
        var bucketHinge = Part("Motor Hinge 2x2 Medium");
        var table = Part("Turntable 3x3 Medium");
        var scoop = Enumerable.Range(0, c.PartCount).First(i => c.DefOf(i).IsScoop);
        var drill = Enumerable.Range(0, c.PartCount).First(i => c.DefOf(i).Name.EndsWith("Drill Module", StringComparison.Ordinal));
        var levers = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Name == "Analog Lever").ToArray();
        var w = server.Constructs;
        var up = UpAt(spot);
        Vector3 Local(Vector3 world) => Vector3.Transform(world - c.Pos, Quaternion.Conjugate(c.Rot));
        Vector3 Edge() => ConstructCargo.FromPart(c, scoop, new Vector3(0, -0.115f, 1.125f));
        var dd = c.DefOf(drill);
        Vector3 Bit()
        {
            // The work box's lowest point along up, as the drill's own step measures it (ConstructGatherer.WorkTip).
            var a = ConstructCargo.FromPart(c, drill, dd.WorkMin);
            var b = ConstructCargo.FromPart(c, drill, dd.WorkMax);
            var u = UpAt(a);
            return (a + b) * 0.5f - u * (MathF.Abs(Vector3.Dot(b - a, u)) * 0.5f);
        }
        float Height(Vector3 p) => Vector3.Dot(p - Ground(p), UpAt(p));
        float Deg(int part) => Joint(part) is var j and >= 0 ? c.Joints[j].Angle * 180 / MathF.PI : float.NaN;
        string Load(int part) => Joint(part) is var j and >= 0 ? $"{c.Joints[j].Over * 100:0}%" : "-";
        int Amount(Construct b, int part) => w.StoreOf(b, part) is { } st ? st.Amount : 0;
        string What(Construct b, int part) => w.StoreOf(b, part) is { } st && st.Amount > 0 ? $"{st.Amount} {RecyclingTable.Current.NameOf(st.Material)}" : "empty";
        void Set(int lever, float value)
        {
            AnalogLever.SetValue(ref c.Parts[levers[lever]], c.DefOf(levers[lever]), value);
            c.Wake();
        }
        string V(Vector3 a) => $"({a.X:0.0} {a.Y:0.0} {a.Z:0.0})";
        Vector3 Dir(Vector3 own) => Vector3.Normalize(Local(ConstructCargo.FromPart(c, scoop, own)) - Local(ConstructCargo.FromPart(c, scoop, Vector3.Zero)));
        string Pose() => $"[cuts {V(Dir(Vector3.UnitZ))} mouth {V(Dir(new Vector3(0, 1.25f, 1)))}] " + $"boom {Deg(boom):0} deg ({Load(boom)}), stick {Deg(stick):0} ({Load(stick)}), bucket {Deg(bucketHinge):0} ({Load(bucketHinge)}), house {Deg(table):0} ({Load(table)}); bucket edge {Height(Edge()):0.00} m over the ground, {-Local(Edge()).Z:0.0} m ahead; drill bit {Height(Bit()):0.00} m";
        // The host's own player into the driver's seat, as the use key puts them there (ConstructEdits.Use): their held keys reach the seat each tick.
        {
            ref var hand = ref server.SandboxHands.Hand(me.Id);
            hand.SeatBody = c.Id;
            hand.SeatPart = (ushort)seatPart;
            c.Parts[seatPart].SitterPlus1 = (byte)(me.Id + 1);
            c.Driver = me.Id;
            c.DriverSeat = seatPart;
            me.Teleport(ConstructEdits.SeatFeet(c, seatPart));
            c.Wake();
        }
        Tick(3);
        Console.WriteLine($"excavator check: {gen.Name} {seed}, {c.PartCount} parts, {c.Mass:0} kg, at {spot.X:0.0} {spot.Y:0.0} {spot.Z:0.0}; {Pose()}");
        Say(MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitY, c.Rot), up)) > 0.97f, $"it stands level on the ground (tilt {MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitY, c.Rot), up), -1, 1)) * 180 / MathF.PI:0.0} deg)");

        // 1. The boom down: the stick hangs straight, the bucket's edge into the ground.
        Set(0, 1);
        Tick(4);
        Console.WriteLine($"  boom lever down: {Pose()}");
        Say(Deg(boom) > 60, $"the boom lever lowered the boom ({Deg(boom):0} deg)");
        float? clientBoom = null;
        if (client != null && client.Constructs.Get(c.Id) is { } copy && Enumerable.Range(0, copy.JointCount).FirstOrDefault(j => copy.Joints[j].Hinge == boom, -1) is var cj and >= 0)
            clientBoom = copy.Joints[cj].Angle * 180 / MathF.PI;
        if (client != null)
            Say(clientBoom is { } cb && MathF.Abs(cb - Deg(boom)) < 3, $"the joined client sees the boom move: its copy at {clientBoom?.ToString("0") ?? "none"} deg, the host's {Deg(boom):0}");

        // 2. Dig: the arm back up, the bucket's edge turned down, the boom lowered into the ground, then the bucket curled through it.
        var ops0 = server.DigOps;
        Set(0, 0);
        Tick(3);
        var best = (Lever: 0.5f, Edge: -2f);
        foreach (var b in new[] { 0f, 1f })
        {
            Set(2, b);
            Tick(2);
            if (-Dir(Vector3.UnitZ).Y > best.Edge)
                best = (b, -Dir(Vector3.UnitZ).Y);
        }
        Set(2, best.Lever);
        Tick(2);
        Console.WriteLine($"  bucket lever to {best.Lever} (edge down): {Pose()}");
        for (var k = 0; k <= 20; k++)
        {
            Set(0, k / 20f);
            Tick(0.25f);
        }
        Console.WriteLine($"  boom lowered into the ground: {Pose()}; bucket {What(c, scoop)}, {server.DigOps - ops0} dig ops");
        for (var k = 0; k <= 20; k++)
        {
            Set(2, best.Lever + (1 - 2 * best.Lever) * k / 20f);
            Set(1, 0.5f + (best.Lever - 0.5f) * k / 20f);
            Tick(0.25f);
        }
        var dug = Amount(c, scoop);
        Console.WriteLine($"  bucket curled through: {Pose()}; bucket {What(c, scoop)}, {server.DigOps - ops0} dig ops");
        Say(dug > 0, $"the bucket dug: {What(c, scoop)} ({server.DigOps - ops0} ground ops)");
        // Out of the ground first (an edge coming out is let go), then the bucket turned mouth up.
        Set(1, 0.5f);
        for (var k = 0; k <= 10; k++)
        {
            Set(0, 1 - 0.85f * k / 10f);
            Tick(0.3f);
        }
        Tick(1);
        Console.WriteLine($"  raised: {Pose()}; bucket {What(c, scoop)}");
        var holdLever = 1 - best.Lever; // (where the curl through left it: mouth up)
        Set(2, holdLever);
        Tick(2);
        var held = Amount(c, scoop);
        Console.WriteLine($"  curled, bucket lever {holdLever}: {Pose()}; bucket {What(c, scoop)}");
        Say(held > 0 && held >= dug * 0.8f, $"the curled bucket held its load up off the ground ({held} of {dug})");

        // 3. Slew: E held from the seat until the house has come round a quarter.
        var h0 = Deg(table);
        roll = 1;
        for (var t = 0; t < 40 && MathF.Abs(Deg(table) - h0) < 88; t++)
        {
            Tick(0.25f);
        }
        roll = 0;
        Tick(1);
        Console.WriteLine($"  slewed: {Pose()}");
        Say(MathF.Abs(Deg(table) - h0) > 60, $"E slewed the house {Deg(table) - h0:0} deg from the seat");

        // 4. The spoil tipped out on open ground beside it: a heap (fill ops), not bundles.
        var pourLever = holdLever < 0.5f ? 1f : 0f;
        var bodies0 = w.Bodies.Count;
        var fillOps0 = server.DigOps;
        Vector3 LowLip()
        {
            var lips = new[] { new Vector3(-0.625f, -0.115f, 1.125f), new Vector3(0.625f, -0.115f, 1.125f), new Vector3(-0.625f, 0.875f, -0.125f), new Vector3(0.625f, 0.875f, -0.125f) }.Select(o => ConstructCargo.FromPart(c, scoop, o)).OrderBy(Height).ToArray();
            return (lips[0] + lips[1]) * 0.5f;
        }
        Set(2, pourLever);
        var pourAt = Vector3.Zero; // (where the lip was when the load began to leave, in the excavator's frame: the hauler goes under it)
        for (var k = 0; k < 50; k++)
        {
            Tick(0.1f);
            if (pourAt == Vector3.Zero && Amount(c, scoop) < dug)
                pourAt = Local(LowLip());
        }
        Console.WriteLine($"  bucket opened over open ground: {Pose()}; bucket {What(c, scoop)}, {server.DigOps - fillOps0} fill ops, {w.Bodies.Count - bodies0} new bodies");
        Say(Amount(c, scoop) < dug && server.DigOps > fillOps0, $"the spoil went onto the ground as a heap: {server.DigOps - fillOps0} fill ops, {Amount(c, scoop)} left in the bucket, {w.Bodies.Count - bodies0} bundle(s)");

        // 5. Ore: the drill's chute fills the curled bucket; a hauler's bed set under it; the bucket opened over the bed.
        Set(2, holdLever);
        Tick(2.5f);
        var ore = RecyclingTable.Current.IdOf("iron ore");
        var fed = w.Deposit(c, drill, 15, ore);
        Tick(6);
        Console.WriteLine($"  {fed} iron ore put in the drill's buffer: bucket {What(c, scoop)}, drill {What(c, drill)}");
        var oreIn = w.StoreOf(c, scoop) is { } sc && sc.Material == ore ? sc.Amount : 0;
        Say(oreIn > 0, $"the drill's chute carried ore into the bucket ({What(c, scoop)})");
        var hb = ConstructBuild.ParseBlueprint(haulerText, "hauler.json");
        var under = Ground(Edge());
        var right = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, c.Rot));
        var hauler = server.SandboxVehicleBuild(255, under + right * 6, MathF.Atan2(right.X, right.Z), hb, null, null, out why);
        if (hauler == null)
        {
            Console.WriteLine($"  the hauler doesn't build: {why}");
            return fails + 1;
        }
        var bed = Enumerable.Range(0, hauler.PartCount).First(i => hauler.DefOf(i).Name == "Tipper Body");
        Tick(2);
        // Under the bucket's mouth: the hauler moved so its bed's middle is under the bucket's middle, and settled (a driver would back it in).
        var mid = c.Pos + Vector3.Transform(pourAt, c.Rot); // (where the lip poured the spoil: the arm is in the same pose)
        var shift = mid - ConstructCargo.FromPart(hauler, bed, Vector3.Zero);
        shift -= Vector3.Dot(shift, up) * up;
        hauler.Pos += shift;
        hauler.Vel = Vector3.Zero;
        hauler.AngVel = Vector3.Zero;
        hauler.Wake();
        Tick(3);
        var rim = Height(ConstructCargo.FromPart(hauler, bed, new Vector3(0, 0.62f, 0)));
        Console.WriteLine($"  hauler's bed rim {rim:0.00} m up; bucket's edge {Height(Edge()):0.00} m; bed {What(hauler, bed)}");
        bodies0 = w.Bodies.Count;
        Set(2, pourLever);
        for (var k = 0; k < 40 && Amount(c, scoop) > 0; k++)
        {
            // A driver backs the bed under the lip as the bucket opens: its middle under the lowest lip, along the ground.
            var miss = LowLip() - ConstructCargo.FromPart(hauler, bed, Vector3.Zero);
            miss -= Vector3.Dot(miss, up) * up;
            if (miss.Length() > 0.4f && k % 3 == 0)
            {
                hauler.Pos += miss;
                hauler.Vel = Vector3.Zero;
                hauler.AngVel = Vector3.Zero;
                hauler.Wake();
            }
            Tick(0.1f);
        }
        Tick(2);
        var poured = w.StoreOf(hauler, bed) is { } hs && hs.Material == ore ? hs.Amount : 0;
        Console.WriteLine($"  bucket opened over the bed: {Pose()}; bucket {What(c, scoop)}, bed {What(hauler, bed)}, {w.Bodies.Count - bodies0} new bodies");
        Say(oreIn > 0 && poured > 0 && w.Bodies.Count == bodies0, $"the ore went into the hauler's bed: {What(hauler, bed)}, {w.Bodies.Count - bodies0} bundle(s) spilt");

        // The drilling pose: slewed back, the bucket curled out of the way, the boom down: the bit on the ground ahead.
        Set(2, holdLever);
        roll = -1;
        for (var t = 0; t < 40 && MathF.Abs(Deg(table) - h0) > 4; t++)
            Tick(0.25f);
        roll = 0;
        Set(0, 1);
        Set(1, 0.5f);
        Tick(5);
        Console.WriteLine($"  drilling pose: {Pose()}");
        Say(Height(Bit()) < 0.3f, $"with the boom down and the bucket curled the drill's bit reaches the ground ({Height(Bit()):0.00} m; a drill works within 0.25 m)");

        // 6. A round world: the drill lowered onto the vein under the excavator's front works it.
        if (pl != null)
        {
            var drillSwitch = Part("Switch");
            c.Parts[drillSwitch].State |= 1;
            c.Wake();
            var bucket0 = Amount(c, scoop);
            var tap = map.Resources?.TappableAt(Bit());
            Tick(10);
            Console.WriteLine($"  drill down on {(tap?.Name ?? "no deposit")}: {Pose()}; drill {What(c, drill)}, bucket {What(c, scoop)}");
            Say(Amount(c, scoop) + Amount(c, drill) > bucket0, $"the drill worked the vein into its buffer and the bucket ({What(c, drill)}; bucket {What(c, scoop)})");
        }
        if (client != null && client.Constructs.Get(c.Id) is { } late)
        {
            var j = Enumerable.Range(0, late.JointCount).FirstOrDefault(k => late.Joints[k].Hinge == table, -1);
            Say(j >= 0 && MathF.Abs(late.Joints[j].Angle * 180 / MathF.PI - Deg(table)) < 3, $"the client's copy follows the slew ({(j >= 0 ? late.Joints[j].Angle * 180 / MathF.PI : float.NaN):0} deg against {Deg(table):0})");
        }
        map.RestoreProps();
        World.SetMap(Maps.All[0]);
        Console.WriteLine(fails == 0 ? $"excavator check: it digs, slews and loads ({clock.Elapsed.TotalSeconds:0} s)" : $"excavator check: {fails} failure(s)");
        return fails;
    }

    /// <summary>A round world's ground over a vein a tier-1 drill can work, searched outwards from the equator: null when none is found.</summary>
    private static Vector3? VeinSpot(Map map, Planetoid pl)
    {
        if (map.Resources is not { } field)
            return null;
        for (var lat = 0; lat <= 60; lat += 2)
            for (var lon = 0; lon < 360; lon += 2)
                foreach (var sign in new[] { 1, -1 })
                {
                    var dir = Vector3.Normalize(Map.SurfaceDir(sign * lat, lon));
                    var g = pl.GroundUnder(pl.Center + dir * (float)(pl.Radius + 50));
                    if (field.TappableAt(g) is { MinTier: <= 1 })
                        return g;
                }
        return null;
    }
}
