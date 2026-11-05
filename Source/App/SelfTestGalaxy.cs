using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Galaxy" (phase 6, docs/space/GALAXY.md): the galaxy tier and the jump drive. The shipped systems load in light-years
    /// (Dev, Ember, Frostreach), an eleventh file and two systems on one spot are refused, and a jump arrives in a circular
    /// orbit at the target's edge. Then a real host and client with lag and loss, Sandbox on the Planetoid: the Jumper
    /// blueprint is put in orbit with the host in its seat; with too little charge the jump key does nothing but say why;
    /// charged, it jumps to Ember (its ship keeps its id and moves to Ember's flight, the batteries pay the energy), the host
    /// sees Ember's sky with the Dev star among the far stars, the client follows the ship there and says where it is; W
    /// still pushes it; it jumps back home; and the tick with a ship away allocates nothing.
    /// </summary>
    private static void CheckGalaxy()
    {
        StarSystems.Reload();
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }

        // ---- The shipped galaxy ----------------------------------------------------------------------------------------
        var names = string.Join(", ", StarSystems.All.Select(e => e.System.Name));
        Line($"galaxy: {Galaxy.Count} systems ({names}); errors {StarSystems.Errors.Count}");
        var dev = StarSystems.All.ToList().FindIndex(e => e.System.File == "Dev.json");
        var ember = StarSystems.All.ToList().FindIndex(e => e.System.File == "Ember.json");
        var frost = StarSystems.All.ToList().FindIndex(e => e.System.File == "Frostreach.json");
        Want(StarSystems.Errors.Count == 0 && dev >= 0 && ember >= 0 && frost >= 0 && Galaxy.Count <= Galaxy.MaxSystems, $"Dev, Ember and Frostreach load ({names}; {string.Join("; ", StarSystems.Errors)})");
        if (dev < 0 || ember < 0 || frost < 0)
        {
            Fail($"galaxy: {string.Join("; ", problems)}");
            return;
        }
        var de = Galaxy.DistanceLy(dev, ember);
        Galaxy.Nearest(dev, 1, out var n1, out _);
        Galaxy.Nearest(dev, 2, out var n2, out _);
        Galaxy.Nearest(ember, 1, out var e1, out _);
        Want(Math.Abs(de - 4.2732) < 0.001 && n1 == ember && n2 == frost && !Galaxy.Nearest(dev, 3, out _, out _) && e1 == dev,
            $"distances and the nearest order (Dev-Ember {de:0.0000} ly; Dev's 1st {n1}, 2nd {n2}; Ember's 1st {e1})");
        // The jump arrives at the edge, on the side it came from, in a circular orbit round the star.
        var t0 = SpaceTime.FromSeconds(1e6);
        var es = Galaxy.System(ember);
        Galaxy.Arrive(dev, ember, t0, out var ap, out var av);
        es.StateOf(0, t0, out var sp, out var sv);
        var ar = ap - sp;
        var circ = Math.Sqrt(es.Bodies[0].Mu / es.Arrival);
        var back = Galaxy.DirectionTo(ember, dev);
        Want(Math.Abs(ar.Length - es.Arrival) < 1 && Math.Abs((av - sv).Length - circ) < 1e-6 && Math.Abs(Vec3d.Dot(ar, av - sv)) < 1e-3 * ar.Length * circ
             && Vec3d.Dot(ar.Normalized(), new Vec3d(back.X, back.Y, 0).Normalized()) > 0.9999,
            $"a jump arrives {es.Arrival / StarSystemFile.Au:0.00} AU out, towards where it came from, in a circular orbit ({ar.Length / StarSystemFile.Au:0.000} AU, {(av - sv).Length:0.0} of {circ:0.0} m/s)");
        // Refused: an eleventh system, two on one spot, a bad key.
        var tmp = Path.Combine(Path.GetTempPath(), $"cos-galaxy-{Environment.ProcessId}");
        Directory.CreateDirectory(tmp);
        try
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, StarSystemFile.FolderName, "Ember.json"));
            for (var k = 0; k < 11; k++)
                File.WriteAllText(Path.Combine(tmp, $"S{k:00}.json"), text.Replace("\"ly\": [3.1, 2.8, 0.9]", $"\"ly\": [{k * 2}, 0, 0]"));
            var errs = new List<string>();
            var got = StarSystems.LoadFolder(tmp, errs);
            Want(got.Count == 10 && errs.Count == 1 && errs[0].Contains("S10.json") && errs[0].Contains("10 star systems"), $"an eleventh system is refused ({got.Count} loaded; {string.Join("; ", errs)})");
            foreach (var f in Directory.GetFiles(tmp))
                File.Delete(f);
            File.WriteAllText(Path.Combine(tmp, "A.json"), text);
            File.WriteAllText(Path.Combine(tmp, "B.json"), text.Replace("\"ly\": [3.1, 2.8, 0.9]", "\"ly\": [3.1, 2.81, 0.9]"));
            errs.Clear();
            got = StarSystems.LoadFolder(tmp, errs);
            Want(got.Count == 1 && errs.Count == 1 && errs[0].Contains("sits on top of"), $"two systems on one spot: the later is refused ({string.Join("; ", errs)})");
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
        try
        {
            StarSystemFile.Load("{ \"bodies\": [ { \"name\": \"S\", \"gm\": 1e20, \"radius\": 1e9 } ], \"galaxy\": { \"ly\": [0, 0, 0], \"warp\": 3 } }", "Bad.json");
            Want(false, "an unknown key in \"galaxy\" is refused");
        }
        catch (StarSystemException ex)
        {
            Want(ex.Message.Contains("unknown key \"warp\""), $"an unknown key in \"galaxy\" is refused ({ex.Message})");
        }

        JumpScene(problems, dev, ember);
        foreach (var p in problems)
            Fail($"galaxy: {p}");
    }

    /// <summary>The jump drive in play: host + client, the Jumper in orbit round the Planetoid, to Ember and back.</summary>
    private static void JumpScene(List<string> problems, int dev, int ember)
    {
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        if (Maps.Find("Planetoid") is not { Planetoid: { } pl } map || ConstructBuild.FindBlueprint("Jumper") is not { } bp)
        {
            problems.Add("the Planetoid map or the Jumper blueprint is missing");
            return;
        }
        const ushort port = 26533;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        PumpFast(server, [a], 0.3, ref now);
        if (a.LocalId == byte.MaxValue || server.Local is not { } me || server.Space is not { Flight: { } flight } space || a.Space == null)
        {
            problems.Add($"no host player, client or bubble (client {a.LocalId}, bubble {server.Space != null}/{a.Space != null})");
            return;
        }
        var lagged = server.Net.SimulateBadNetwork(40, 8, 3) & a.Net.SimulateBadNetwork(40, 8, 3);
        Want(space.GalaxyIndex == dev && a.Space.GalaxyIndex == dev && a.Space.GalaxyOk, $"both peers place the bubble in the Dev system of one galaxy (host {space.GalaxyIndex}, client {a.Space.GalaxyIndex}, same {a.Space.GalaxyOk})");
        me.ImmuneUntil = now + 1e6;
        server.RequestRespawn();
        for (var wait = 0; wait < 40 && !me.Alive; wait++)
            PumpFast(server, [a], 0.1, ref now);
        foreach (var c0 in server.Constructs.Bodies.ToList())
            server.Constructs.Remove(c0);
        var centre = pl.Center;
        var edge = server.SpaceEdge;
        var pad = pl.GroundUnder(centre + new Vector3(20, (float)pl.Radius + 400, 20));
        var ship = ConstructBuild.Build(server.Constructs, bp, pad + new Vector3(0, 2.6f, 0), Quaternion.Identity, out var why);
        if (ship == null || !me.Alive)
        {
            problems.Add($"the Jumper didn't build ({why}) or the host didn't spawn");
            return;
        }
        ship.Rebuild();
        int seat = -1, drive = -1;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.Parts[i].D.Role == PartRole.Seat)
                seat = i;
            else if (ship.Parts[i].D.IsJumpDrive)
                drive = i;
        Want(seat >= 0 && drive >= 0 && ship.JumpCount == 1 && ship.ChargeCapacity > 0 && ship.ThrusterCount == 6 && ConstructPower.On(ship, drive),
            $"the Jumper has a seat, 6 jets and a powered jump drive (seat {seat}, drive {drive}, {ship.ThrusterCount} jets, electrical {ship.Electrical}, {ship.Mass:0} kg, {ship.ChargeCapacity:0} kJ)");
        if (seat < 0 || drive < 0)
            return;
        ref var hand = ref server.SandboxHands.Hand(me.Id);
        hand.SeatBody = ship.Id;
        hand.SeatPart = (ushort)seat;
        ship.Parts[seat].SitterPlus1 = (byte)(me.Id + 1);
        ship.Driver = me.Id;
        ship.DriverSeat = seat;
        ship.Wake();
        PumpFast(server, [a], 1.0, ref now);
        // On the ground the drive won't charge.
        void Press()
        {
            server.HeldButtons = Buttons.Jump;
            PumpFast(server, [a], 0.1, ref now);
            server.HeldButtons = Buttons.None;
            PumpFast(server, [a], 0.1, ref now);
        }
        Press();
        Want((ship.Parts[drive].State & 1) == 0 && ship.Parts[drive].Count == 0, "on the ground the jump key doesn't charge the drive");
        // Into orbit: just over the edge, going round.
        var mu = flight.System.Bodies[space.Bubble.Body].Mu;
        var r0 = edge + 200f;
        ship.Pos = centre + new Vector3(0, r0, 0) - Vector3.Transform(ship.Com, ship.Rot);
        ship.Vel = new Vector3((float)Math.Sqrt(mu / r0), 0, 0);
        ship.Wake();
        for (var wait = 0; wait < 40 && ship.SpaceShip == 0; wait++)
            PumpFast(server, [a], 0.05, ref now);
        var id = ship.SpaceShip;
        Want(id != 0 && flight.Ships.IndexOfId(id) >= 0, $"the Jumper is a ship in orbit (ship {id})");
        if (id == 0)
            return;

        // ---- Too little charge: the key says why and nothing happens -----------------------------------------------------
        var why1 = server.JumpProblem(space, ship, drive, out var to, out var ly, out var cost);
        Line($"galaxy: the Jumper: {ship.Mass:0} kg, {ship.ChargeCapacity:0} kJ; target 1 is {(to >= 0 ? Galaxy.System(to).Name : "none")} at {ly:0.00} ly for {cost:0} kJ ({why1 ?? "can go"})");
        Want(why1 == null && to == ember && cost > 50 && cost < ship.ChargeCapacity * 0.6f, $"target 1 is Ember, within reach and charge ({why1}; {cost:0} of {ship.ChargeCapacity:0} kJ)");
        var full = ship.Charge;
        ship.Charge = cost * 0.5f;
        Press();
        var line = server.SpaceJumpLine(ship);
        Want((ship.Parts[drive].State & 1) == 0 && ship.SpaceAway == 0 && line != null && line.Contains("kJ, the batteries hold"), $"with too little charge it won't charge, and says why ({line})");
        ship.Charge = full;

        // ---- To Ember -------------------------------------------------------------------------------------------------
        Press();
        Want((ship.Parts[drive].State & 1) != 0, "the jump key starts the charge");
        var charging = server.SpaceJumpLine(ship);
        PumpFast(server, [a], 4.0, ref now);
        var half = ship.Parts[drive].Out;
        for (var wait = 0; wait < 120 && ship.SpaceAway == 0; wait++)
            PumpFast(server, [a], 0.1, ref now);
        var away = space.Away[ember];
        var ai = away?.Ships.IndexOfId(id) ?? -1;
        var es = Galaxy.System(ember);
        var dist = ai >= 0 ? (away!.Ships.PositionOf(ai) - es.PositionOf(0, away.Ships.Time)).Length : 0;
        Line($"galaxy: charging ({charging}), {half * 100:0}% after 4 s; jumped: away {ship.SpaceAway}, ship {ai} in Ember's flight {dist / StarSystemFile.Au:0.000} AU from Ember, charge {full:0} to {ship.Charge:0} kJ");
        Want(half > 0.35f && half < 0.65f, $"the drive charges over its time ({half * 100:0}% after 4 of 8 s)");
        Want(ship.SpaceAway == ember + 1 && ai >= 0 && flight.Ships.IndexOfId(id) < 0 && ship.SpaceShip == id, $"it jumps: the ship moves to Ember's flight with its id (away {ship.SpaceAway}, index {ai})");
        Want(Math.Abs(dist - es.Arrival) < es.Arrival * 0.01, $"it arrives at Ember's edge ({dist / StarSystemFile.Au:0.000} of {es.Arrival / StarSystemFile.Au:0.000} AU)");
        Want(full - ship.Charge > cost * 0.95f, $"the batteries pay the jump ({full - ship.Charge:0} kJ of {cost:0})");
        Want(server.SpaceSeated(me), "the driver is still seated");

        // The host's view: Ember's sky, the Dev star among the far stars, the sun where Ember's star is.
        var t = server.SpaceRenderTime;
        var viewOk = server.SpaceAwayView(t, out var vsys, out var vpose, out var vg, out var vbody);
        var view = new SpaceSkyView();
        if (viewOk)
            view.Update(vsys, vpose, -1, t, ship.WorldCom);
        var devStar = -1;
        for (var k = 0; k < view.StarCount; k++)
            if (view.Stars[k].Body == dev)
                devStar = k;
        var wantDev = vpose.Rot.InverseRotate(Galaxy.DirectionTo(ember, dev)).ToVector3();
        var sunWant = Vector3.Normalize((vpose.FromEcliptic(es.PositionOf(0, t)) - Vec3d.From(ship.WorldCom)).ToVector3());
        Want(viewOk && vg == ember && ReferenceEquals(vbody, ship) && view.StarCount == Galaxy.Count - 1 && devStar >= 0
             && Vector3.Dot(view.Stars[devStar].Dir, wantDev) > 0.9999f && Vector3.Dot(view.StarDirection, sunWant) > 0.9999f && view.BodyCount == es.Count - 1,
            $"the host rides in Ember's sky: its bodies, its star as the sun and the Dev star among {view.StarCount} far stars (view {viewOk}, galaxy {vg}, bodies {view.BodyCount})");
        // The client follows it there. (PumpFast's 1.5 game seconds are about 0.1 s of real time, and the lag is real: wait for the news, bounded.)
        PumpFast(server, [a], 1.5, ref now);
        for (var wait = 0; wait < 60 && a.Constructs.Get(ship.Id)?.SpaceAway != ember + 1; wait++)
            Pump(server, [a], 0.1, ref now, clock);
        var copy = a.Constructs.Get(ship.Id);
        var th = flight.Clock.Now;
        var cpos = default(SpacePos);
        var cg = -1;
        var seenOk = copy != null && a.SpaceAwayShip(copy, th, out cg, out cpos, out _) && cg == ember;
        ai = away!.Ships.IndexOfId(id);
        var off = seenOk && ai >= 0 ? (cpos - away.Ships.PositionOf(ai)).Length : double.NaN;
        var cline = copy == null ? null : a.SpaceAwayLine(copy);
        Line($"galaxy: the client: away {copy?.SpaceAway}, its ship {off:0.0} m from the host's; line: {cline ?? "none"}; lag and loss {(lagged ? "on" : "NOT on")}");
        Want(copy is { SpaceAway: var ca } && ca == ember + 1 && off < 50 && cline != null && cline.StartsWith("In space in Ember"),
            $"the client follows the ship in Ember and says where it is (away {copy?.SpaceAway}, {off:0.0} m off, {cline})");

        // W still pushes it, in Ember's axes.
        var v0 = away.Ships.VelocityOf(ai);
        server.HeldButtons = Buttons.Forward;
        PumpFast(server, [a], 1.0, ref now);
        server.HeldButtons = Buttons.None;
        PumpFast(server, [a], 0.5, ref now);
        ai = away.Ships.IndexOfId(id);
        var dv = ai >= 0 ? (away.Ships.VelocityOf(ai) - v0).Length : 0;
        var grav = es.Bodies[0].Mu / (es.Arrival * es.Arrival) * 1.5;
        Want(dv > grav * 3 + 1, $"W pushes the ship in Ember ({dv:0.00} m/s in 1.5 s; gravity alone {grav:0.00})");

        // ---- No allocation with a ship away ----------------------------------------------------------------------------
        server.SpaceAwayBefore(space);
        flight.Tick(space.AwayWarpLimit);
        away.Follow(flight.Clock);
        server.SpaceJumpStep(space);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 300; k++)
        {
            server.SpaceAwayBefore(space);
            flight.Tick(space.AwayWarpLimit);
            away.Follow(flight.Clock);
            server.SpaceJumpStep(space);
        }
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        Want(alloc == 0, $"the space tick with a ship away allocates nothing ({alloc} B in 300 ticks)");

        // ---- Home again ------------------------------------------------------------------------------------------------
        ship.Charge = ship.ChargeCapacity;
        Press();
        for (var wait = 0; wait < 120 && ship.SpaceAway != 0; wait++)
            PumpFast(server, [a], 0.1, ref now);
        var hi = flight.Ships.IndexOfId(id);
        var ds = Galaxy.System(dev);
        var hd = hi >= 0 ? (flight.Ships.PositionOf(hi) - ds.PositionOf(0, flight.Ships.Time)).Length : 0;
        PumpFast(server, [a], 1.0, ref now);
        Line($"galaxy: home: parked {ship.SpaceAway}, ship {hi} in the Dev flight {hd / StarSystemFile.Au:0.000} AU from {ds.Bodies[0].Name}; Ember's flight holds {away.Ships.Count}; the client's copy parked {copy?.SpaceAway}");
        Want(ship.SpaceAway == dev + 1 && hi >= 0 && away.Ships.Count == 0 && Math.Abs(hd - ds.Arrival) < ds.Arrival * 0.01 && copy is { SpaceAway: var cb } && cb == dev + 1,
            $"it jumps home: back in the Dev system's flight at its edge, parked (far out), and the client knows (parked {ship.SpaceAway}, index {hi}, client {copy?.SpaceAway})");
        // Brought back within reach of the bubble, it is posed from its ship again.
        if (hi >= 0)
        {
            var body = space.Bubble.Body;
            var bpose = space.Bubble.PoseAt(flight.Ships.Time);
            var rr = bpose.ToEcliptic(Vec3d.From(centre + new Vector3(0, r0 + 2000, 0))) - flight.Ships.BodyPosition(body);
            flight.Ships.SetState(hi, body, rr, Vec3d.Cross(rr.Normalized(), Vec3d.UnitZ) * Math.Sqrt(mu / rr.Length));
        }
        for (var wait = 0; wait < 20 && ship.SpaceAway != 0; wait++)
            PumpFast(server, [a], 0.1, ref now);
        PumpFast(server, [a], 0.3, ref now);
        var near = Vector3.Distance(ship.WorldCom, centre);
        Want(ship.SpaceAway == 0 && ship.SpaceShip == id && MathF.Abs(near - (r0 + 2000)) < 50, $"back within reach it is unparked and posed from its ship ({ship.SpaceAway}, {near - r0:0} m over where it went out)");
    }
}
