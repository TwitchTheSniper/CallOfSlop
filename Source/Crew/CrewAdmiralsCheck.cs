using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-fleet escort [--seconds S] [--escort-ship NAME] [--miner NAME]</c> (docs/sandbox/design/faction-controllers.md phase 6, its
/// done-when): a player's three hired ships ordered to escort their mining ship, which flies a mine-and-deliver route under its own hired crew; a
/// raider comes for the miner. With no game (a tool's world): the player is a point at the miner with a radio's reach. Prints how close each escort
/// keeps its slot before the fight, what they do when the raider is near, and a verdict.
/// </summary>
public static class CrewAdmiralsCheck
{
    public static bool Run(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 150;
        if (int.TryParse(Arg("--every"), out var ev) && ev > 0)
            DebugEvery = ev;
        return Escort(seconds, Arg("--escort-ship") ?? "Zero Day Frigate", Arg("--miner") ?? "Magpie Mining Ship", args.Contains("--no-raider"));
    }

    public static int DebugEvery = 10;

    /// <summary>Seconds the escort has to form up on the miner before it sets off (as a player would wait for them).</summary>
    private const int FormUp = 25;

    private static ConstructBuild.Blueprint Bp(string name) => ConstructBuild.FindBlueprint(name) ?? throw new InvalidOperationException($"no blueprint \"{name}\"");

    private static bool Escort(int seconds, string escortName, string minerName, bool noRaider)
    {
        Console.WriteLine($"fleet escort: the player's three hired {escortName}s escort their {minerName} on a mining route (mine 1.2 km north, deliver 1.2 km east); a Raiders frigate comes for the miner{(noRaider ? " (not: --no-raider)" : " a minute after it sets off")}");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var own = CrewPowers.ForKindName("Player Crew");
        var raiders = CrewPowers.ForKindName("Raiders");
        const byte me = 0;
        var miner = CrewCheck.Build(w, Bp(minerName), Vector3.Zero, 0, place);
        miner.Owner = me;
        crew.Crew(miner, ShipAiMode.Strangers, faction: own);
        var escorts = new List<Construct>();
        for (var i = 0; i < 3; i++)
        {
            var c = CrewCheck.Build(w, Bp(escortName), new Vector3((i - 1) * 110, 0, 160), 0, place);
            c.Owner = me;
            crew.Crew(c, ShipAiMode.Strangers, faction: own);
            escorts.Add(c);
        }
        Construct? raider = null;
        var raiderAt = noRaider ? int.MaxValue : (FormUp + 60) * World.TickRate;
        crew.FixedFor = (Construct c, CrewShip cs, out ShipAiTarget t) =>
        {
            t = default;
            if (raider == null || raider.Wreck)
                return false;
            if (c == raider)
            {
                t = new ShipAiTarget { At = miner.WorldCom, Vel = miner.Vel, Body = miner.Id, Player = 255 };
                return true;
            }
            if (c == miner || Vector3.Distance(c.WorldCom, raider.WorldCom) > 1500)
                return false;
            t = new ShipAiTarget { At = raider.WorldCom, Vel = raider.Vel, Body = raider.Id, Player = 255 };
            return true;
        };
        Span<ushort> one = [miner.Id];
        var gm = crew.Fleet.FormPlayer(crew, w, "Tester", 1, me, one, out var why1);
        Span<ushort> three = [escorts[0].Id, escorts[1].Id, escorts[2].Id];
        var ge = crew.Fleet.FormPlayer(crew, w, "Tester", 1, me, three, out var why2);
        if (gm < 0 || ge < 0)
        {
            Console.WriteLine($"fleet escort: no group formed ({why1 ?? why2}): FAILED");
            return false;
        }
        var mg = crew.Fleet.Groups[gm];
        var eg = crew.Fleet.Groups[ge];
        mg.Task = GroupTask.Mining;
        crew.Fleet.CommandPlayer(mg, OrderKind.Hold, default, 0); // (on its pad while its escort forms on it)
        crew.Fleet.CommandPlayer(eg, OrderKind.Escort, default, miner.Id);
        Flush(crew);
        var people = new[] { Vector3.Zero };
        var minerHp0 = CrewCheck.Health(miner);
        var offs = new List<float>();
        var engaged = 0;
        var looks = 0;
        var hp0 = 0f;
        var raiderHit = false;
        var minerUp = 0;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            if (t == FormUp * World.TickRate)
            {
                crew.Fleet.AddStop(mg, new Vector3(0, 0, -1200), OrderKind.Mine, 15);
                crew.Fleet.AddStop(mg, new Vector3(1200, 0, -1200), OrderKind.Deliver, 15);
                Console.WriteLine($"  {t / World.TickRate,3} s: the miner sets off on its route");
            }
            if (t == raiderAt)
            {
                var ahead = miner.WorldCom + new Vector3(0, 0, -2200);
                raider = CrewCheck.Build(w, Bp("Zero Day Frigate"), ahead with { Y = 0 }, MathF.PI, place);
                crew.Crew(raider, ShipAiMode.Everyone, faction: raiders);
                hp0 = CrewCheck.Health(raider);
                Console.WriteLine($"  {t / World.TickRate,3} s: a Raiders frigate (construct {raider.Id}) sets down 2.2 km ahead of the miner");
            }
            people[0] = miner.WorldCom;
            foreach (var g in new[] { mg, eg })
            {
                g.CommanderHere = true;
                g.CommanderAt = miner.WorldCom;
                g.CommanderReach = crew.Fleet.Rules.RadioRange; // (the player aboard a ship with a radio)
            }
            Tick(crew, w, q, place, people);
            Flush(crew);
            if (miner.WorldCom.Y > 20)
                minerUp++;
            var near = raider != null && !raider.Wreck && Vector3.Distance(raider.WorldCom, miner.WorldCom) < 1200;
            if (t % World.TickRate == 0 && t > FormUp * World.TickRate && !near && raider == null)
                foreach (var c in escorts)
                    if (crew.ShipOf(c.Id) is { Order.Kind: OrderKind.Escort } cs)
                        offs.Add(SlotGap(miner, c, cs.Order));
            if (near && t % World.TickRate == 0)
            {
                looks++;
                foreach (var c in escorts)
                    if (crew.ShipOf(c.Id) is { } cs && (cs.Order.Kind == OrderKind.Engage || cs.Order.Kind == OrderKind.Escort && cs.TargetTick > crew.Tick - 2 * World.TickRate))
                        engaged++;
            }
            if (raider != null && CrewCheck.Health(raider) < hp0 * 0.97f)
                raiderHit = true;
            if (t % (DebugEvery * World.TickRate) == DebugEvery * World.TickRate - 1)
            {
                var line = string.Join("; ", escorts.Select(c => crew.ShipOf(c.Id) is { } cs
                    ? $"{c.Id} {cs.Order.Kind}{(cs.Order.Body != 0 ? $" {cs.Order.Body}" : "")} {SlotGap(miner, c, cs.Order):0} m off (slot {cs.Order.At.X:0},{cs.Order.At.Z:0}), {c.WorldCom.Y:0} m up, vy {c.Vel.Y:0}, tilt {Vector3.Transform(Vector3.UnitY, c.Rot).Y:0.00}, broken {Broken(c)}, fuel {c.FuelLeft:0}/{c.FuelCapacity():0}, power {(c.ChargeCapacity > 0 ? c.Charge / c.ChargeCapacity * 100 : -1):0}% ({cs.Ai?.State ?? "no driver"}, helm {(cs.HelmOn(crew) ? "manned" : "EMPTY")}, {cs.Ai?.Debug})" : "gone"));
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: miner {crew.ShipOf(miner.Id)?.Order.Kind} ({crew.ShipOf(miner.Id)?.Ai?.State}, helm {(crew.ShipOf(miner.Id)?.HelmOn(crew) == true ? "manned" : "EMPTY")}) at {miner.WorldCom.X:0},{miner.WorldCom.Y:0},{miner.WorldCom.Z:0} {miner.Vel.Length():0} m/s; {line}{(raider != null ? $"; raider {CrewCheck.Health(raider) / hp0 * 100:0}% at {Vector3.Distance(raider.WorldCom, miner.WorldCom):0} m" : "")}");
            }
        }
        offs.Sort();
        var median = offs.Count > 0 ? offs[offs.Count / 2] : float.MaxValue;
        var worst = offs.Count > 0 ? offs[(int)(offs.Count * 0.9f)] : float.MaxValue;
        var held = median < 45 && worst < 150; // (settled, 15-25 m: its slot band; past it only while a turn swings the slots round)
        var fought = noRaider || looks > 0 && engaged > 0 && raiderHit;
        var flew = minerUp > 30 * World.TickRate;
        var ok = held && fought && flew;
        Console.WriteLine($"fleet escort: slots kept before the fight median {median:0} m, 90th {worst:0} m ({offs.Count} looks): {(held ? "held" : "LOOSE")}; " +
                          $"the miner flew its route: {(flew ? "yes" : "NO")}; with the raider near, escorts fighting {engaged} of {looks * 3} looks, the raider hit: {(raiderHit ? "yes" : "NO")}{(raider != null ? $" ({CrewCheck.Health(raider) / Math.Max(1, hp0) * 100:0}% left{(raider.Wreck ? ", a wreck" : "")})" : "")}, the miner {CrewCheck.Health(miner) / minerHp0 * 100:0}% whole{(crew.ShipOf(miner.Id)?.HelmOn(crew) == true ? "" : ", its helm lost")}: {(ok ? "OK" : "FAILED")}; " +
                          $"admirals {crew.Fleet.CpuMs / Math.Max(1, crew.Fleet.Ticks) * 1000:0.0} us a tick (worst {crew.Fleet.WorstMs * 1000:0} us), {crew.Fleet.Bytes} bytes");
        return ok;
    }

    private static int Broken(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Broken)
                n++;
        return n;
    }

    /// <summary>How far a ship is from the slot its Escort or Form order gives it (level), as CrewOrders.cs places it.</summary>
    private static float SlotGap(Construct guard, Construct c, in ShipOrder o)
    {
        if (o.Kind is not (OrderKind.Escort or OrderKind.Form))
            return float.NaN;
        var up = GravityFrames.UpAt(guard.WorldCom);
        var fwd = Vector3.Transform(guard.StabCount > 0 ? -Vector3.UnitZ : guard.DriveFwd, guard.Rot);
        fwd -= up * Vector3.Dot(fwd, up);
        fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.UnitZ;
        var right = Vector3.Cross(fwd, up);
        var to = guard.WorldCom + right * o.At.X + fwd * o.At.Z;
        var d = to - c.WorldCom;
        d -= up * Vector3.Dot(d, up);
        return d.Length();
    }

    private static void Flush(CrewWorld crew)
    {
        foreach (var e in crew.Events)
            if (e.StartsWith("fleet:", StringComparison.Ordinal) || e.Contains("helm", StringComparison.Ordinal))
                Console.WriteLine($"  [crew] {e}");
        crew.Events.Clear();
        foreach (var n in crew.Fleet.Notices)
            Console.WriteLine($"  [to {n.Owner}] {n.Text}");
        crew.Fleet.Notices.Clear();
        crew.Fleet.News.Clear();
    }

    private static void Tick(CrewWorld crew, ConstructWorld w, SelfTest.DriveWorld q, VehicleDriveCheck.CheckPlace place, Vector3[] people)
    {
        using (place.In())
        {
            crew.Step(w, q, null, people, crewOn: false);
            w.Step(q, World.TickDt);
        }
    }
}
