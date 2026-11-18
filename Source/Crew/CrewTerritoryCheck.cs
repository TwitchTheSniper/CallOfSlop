using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-fleet territory [--seconds S]</c> and <c>--crew-fleet far [--seconds S]</c> (docs/sandbox/design/faction-controllers.md phase 4).
/// <c>territory</c>: 16 powers (temporary power files: Traders, Raiders and Militia, the Raiders at war with the rest) over five planets of eight key sites,
/// with nobody anywhere: their controllers send Take and Defend groups nobody sees, which travel, fight by strength and take sites; a planet changes
/// hands and the news says so. The controllers' and the far layer's cost a tick is timed against 0.3 ms with at least 64 far groups live.
/// <c>far</c>: a Raiders group making for a Militia site 6 km from a person on the tool's world: nothing is set down while the person is far, the group
/// materialises out of sight as the person walks towards it, and collapses back to records (hulls kept) when the person walks away.
/// </summary>
public static class CrewTerritoryCheck
{
    private const int MaxShipsFor = PowerControllers.MaxShips;

    private sealed class Host(ConstructWorld w, CrewWorld crew, VehicleDriveCheck.CheckPlace place) : IPowerHost, IPowerSight
    {
        public PowerControllers Ctl = null!;
        public readonly List<Vector3> Persons = new();
        public string World = "nowhere";
        public int Spawns, Docks;
        public float NearestSpawn = float.MaxValue;
        public readonly Dictionary<string, Vector2> Sites = new();

        public int People(Span<Vector3> into)
        {
            var n = 0;
            foreach (var p in Persons)
                if (n < into.Length)
                    into[n++] = p;
            return n;
        }

        public string WorldKey => World;

        public bool Ground(Vector3 at, out Vector3 ground)
        {
            ground = at with { Y = 0 };
            return true;
        }

        public bool Spawn(byte power, int ship, ConstructBuild.Blueprint bp, Vector3 at, float yaw)
        {
            foreach (var p in Persons)
                NearestSpawn = MathF.Min(NearestSpawn, Vector3.Distance(p, at));
            var c = CrewCheck.Build(w, bp, at, yaw, place);
            crew.Crew(c, ShipAiMode.Strangers, faction: power);
            Spawns++;
            Ctl.Spawned(crew, power, ship, c, "");
            return true;
        }

        public void Dock(Construct c)
        {
            if (crew.ShipOf(c.Id) is { } s)
                crew.Disband(s, w, null, "collapsed");
            w.Remove(c);
            Docks++;
        }

        public bool Sees(byte power, Vector3 at) => false;

        public bool SiteAt(string planet, Vector2 at, out Vector3 world)
        {
            world = new Vector3(at.X, 0, at.Y);
            return planet.Equals(World, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool Run(string which, int seconds) => which.Equals("far", StringComparison.OrdinalIgnoreCase) ? FarCheck(seconds > 0 ? seconds : 240) : Territory(seconds > 0 ? seconds : 600);

    private static string PowerFile(string name, string kind, string tag, IEnumerable<(string, int)> relations) =>
        $"{{ \"format\": \"call-of-slop-npc-power/1\", \"name\": \"{name}\", \"kind\": \"{kind}\", \"tag\": \"{tag}\", \"colour\": \"#808080\", " +
        $"\"parameters\": {{ \"aggression\": 0.7, \"caution\": 1.0 }}, \"relations\": {{ {string.Join(", ", relations.Select(r => $"\"{r.Item1}\": {r.Item2}"))} }}, \"controller\": true }}";

    private static bool Territory(int seconds)
    {
        Console.WriteLine("fleet territory: 16 powers over five planets of eight key sites, nobody anywhere; the far layer runs it all");
        var dir = Path.Combine(Path.GetTempPath(), $"cos-powers-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var names = new List<(string Name, string Kind)>();
        for (var i = 0; i < 16; i++)
        {
            var kind = (i % 3) switch { 0 => "Raiders", 1 => "Traders", _ => "Militia" };
            names.Add(($"{kind} {i / 3 + 1}", kind));
        }
        foreach (var (name, kind) in names)
        {
            var rels = names.Where(o => o.Name != name && (kind == "Raiders") != (o.Kind == "Raiders")).Select(o => (o.Name, -70));
            File.WriteAllText(Path.Combine(dir, $"{name}.json"), PowerFile(name, kind, name[..1] + name[^1], rels));
        }
        CrewPowers.Use(dir);
        if (CrewPowers.Count != 16)
        {
            Console.WriteLine($"fleet territory: FAILED: {CrewPowers.Count} powers read, not 16");
            return false;
        }
        var t = new TerritoryRules { TransitSeconds = 20, LandSeconds = 10, TakeSeconds = 20, TakeEveryMinutes = 0.25f, GatherMinutes = 0.3f, HoldMinutes = 1, FarGroups = 16, TakeShips = 3, FightRate = 0.05f, FightCap = 0.05f };
        t.Kinds.Add(new SiteKindDef { Name = "spaceport", Weight = 10, Garrison = 4, Income = 20 });
        t.Kinds.Add(new SiteKindDef { Name = "mine", Weight = 4, Garrison = 2, Income = 8 });
        t.Kinds.Add(new SiteKindDef { Name = "outpost", Weight = 3, Garrison = 2, Income = 3 });
        for (var pl = 0; pl < 5; pl++)
        {
            t.Planets.Add($"P{pl + 1}");
            for (var k = 0; k < 8; k++)
                t.Sites.Add(new SiteDef { Name = $"P{pl + 1} site {k + 1}", Planet = $"P{pl + 1}", Kind = k == 0 ? 0 : k < 5 ? 1 : 2, Holder = names[(pl * 3 + (k < 4 ? 0 : k < 7 ? 1 : 2)) % 16].Name });
        }
        var rules = ControllerRules.Load(Path.Combine(AppFolders.Data, "Crew", "Controllers.json"));
        (rules.MinBuildSeconds, rules.BuildSecondsScale) = (2, 0.002f);
        foreach (var plan in rules.Kinds.Values)
            (plan.Ships, plan.Start) = (MaxShipsFor, 60000);
        var w = new ConstructWorld();
        var crew = new CrewWorld();
        var place = VehicleDriveCheck.Place(1);
        var rel = new PowerRelations { Table = new FactionTable() };
        var ctl = new PowerControllers { Rules = rules, Territory = t };
        ctl.Reset();
        rel.Reset();
        var host = new Host(w, crew, place) { Ctl = ctl };
        rel.Sight = host;
        rel.Might = ctl.Might;
        Console.Write(ctl.TerritoryText());
        var holders0 = (byte[])ctl.PlanetHolder.Clone();
        var news = new List<string>();
        int mostFar = 0, ticksAt64 = 0;
        double msAt64 = 0, worstAt64 = 0;
        var perTick = new List<double>(seconds * World.TickRate);
        var b0 = 0L;
        for (var tick = 0; tick < seconds * World.TickRate; tick++)
        {
            if (tick == 60 * World.TickRate)
                (ctl.Bytes, ctl.FarBytes, b0) = (0, 0, GC.GetAllocatedBytesForCurrentThread());
            // Past two minutes, the far groups are topped up to 64 (each power's caps raised for it): the cost is timed at that count.
            if (tick > 120 * World.TickRate && ctl.FarLive < 64 && tick % 7 == 0)
                TopUp(ctl, rel, t, tick);
            var s0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ctl.Step(crew, w, rel, host);
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            rel.Step();
            mostFar = Math.Max(mostFar, ctl.FarLive);
            if (ctl.FarLive >= 64 && tick > 60 * World.TickRate)
            {
                ticksAt64++;
                msAt64 += ms;
                worstAt64 = Math.Max(worstAt64, ms);
            }
            if (tick > 60 * World.TickRate)
                perTick.Add(ms);
            foreach (var n in ctl.News)
            {
                news.Add(n);
                if (n.Contains("falls to") || n.Contains("is contested") || n.Contains("holds ") || news.Count < 12)
                    Console.WriteLine($"  {tick / World.TickRate,4} s [news] {n}");
            }
            ctl.News.Clear();
            ctl.Log.Clear();
            crew.Events.Clear();
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        Console.Write(ctl.TerritoryText());
        perTick.Sort();
        var changed = 0;
        for (var pl = 0; pl < ctl.PlanetCount; pl++)
            changed += ctl.PlanetHolder[pl] != holders0[pl] ? 1 : 0;
        var avg = perTick.Count > 0 ? perTick.Average() : 0;
        Console.WriteLine($"fleet territory: {news.Count} news lines; {ctl.SitesTaken} sites taken, {ctl.PlanetChanges} planet changes ({changed} planets held by another now); far groups at most {mostFar}; " +
                          $"{ctl.FarSteps} group steps, {ctl.FarFights} fight steps");
        Console.WriteLine($"fleet territory: controllers and the far layer {avg * 1000:0.0} us a tick past the first minute (median {perTick[perTick.Count / 2] * 1000:0.0} us, 99th {perTick[perTick.Count * 99 / 100] * 1000:0} us, worst {perTick[^1] * 1000:0} us); " +
                          $"with 64 or more far groups: {ticksAt64} ticks, {(ticksAt64 > 0 ? msAt64 / ticksAt64 * 1000 : 0):0.0} us a tick, worst {worstAt64 * 1000:0} us; " +
                          $"bytes in quiet steps: thinks {ctl.Bytes}, far {ctl.FarBytes} ({bytes} in all, with news)");
        // A round trip of the section's bytes.
        var bytes1 = ctl.SaveBytes();
        var back = new PowerControllers { Rules = rules, Territory = t };
        back.Load(new PowerRelations(), bytes1);
        var same = back.SaveBytes().AsSpan().SequenceEqual(bytes1);
        Console.WriteLine($"fleet territory: the section ({bytes1.Length} bytes) reads back {(same ? "the same" : "DIFFERENT")}");
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
        CrewPowers.Use(Path.Combine(AppFolders.Data, "Crew", "Powers"));
        var ok = changed > 0 && ticksAt64 > 0 && msAt64 / Math.Max(1, ticksAt64) < 0.3 && avg < 0.3 && same;
        Console.WriteLine($"fleet territory: a planet changed hands with nobody there: {(changed > 0 ? "yes" : "NO")}; 16 powers and 64 groups under 0.3 ms: {(ticksAt64 > 0 && msAt64 / ticksAt64 < 0.3 ? "yes" : "NO")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    private static void TopUp(PowerControllers ctl, PowerRelations rel, TerritoryRules t, int tick)
    {
        var a = (byte)(1 + tick / 7 % 16);
        var p = ctl.Powers[a];
        for (var k = 0; k < ctl.SiteCount; k++)
        {
            var h = ctl.SiteHolder[(k + tick) % ctl.SiteCount];
            if (h == 0 || h == a || !rel.Fights(a, h))
                continue;
            var role = CrewPowers.KindOf(a)!.Name switch { "Raiders" => ShipRole.Raider, "Traders" => ShipRole.Escort, _ => ShipRole.Warship };
            p.Stock = Math.Max(p.Stock, 5000); // (a tool's: the books aren't the point here)
            ctl.FarForm(rel, p, a, FarTask.Take, (k + tick) % ctl.SiteCount, 2, role);
            return;
        }
    }

    private static bool FarCheck(int seconds)
    {
        Console.WriteLine("fleet far: a Raiders group making for a Militia site 6 km from a person; the person walks up to it, then away");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var rel = new PowerRelations { Table = new FactionTable() };
        byte raiders = CrewPowers.ForKindName("Raiders"), militia = CrewPowers.ForKindName("Militia");
        var t = new TerritoryRules { TransitSeconds = 2, LandSeconds = 2, TakeSeconds = 30, TakeEveryMinutes = 100, CollapseSeconds = 10, GroundSpeed = 20, SiteReach = 300, FarGroups = 2, MaterialiseRange = 5000, CollapseRange = 7000 };
        t.Kinds.Add(new SiteKindDef { Name = "outpost", Weight = 3, Garrison = 2, Income = 3 });
        t.Planets.Add("check");
        t.Sites.Add(new SiteDef { Name = "Check Post", Planet = "check", Kind = 0, At = new Vector2(0, -6000), Holder = CrewPowers.Of(militia)!.Name });
        var rules = ControllerRules.Load(Path.Combine(AppFolders.Data, "Crew", "Controllers.json"));
        (rules.MinBuildSeconds, rules.BuildSecondsScale) = (1, 0.001f);
        foreach (var plan in rules.Kinds.Values)
            (plan.Convoy, plan.Raid, plan.Patrol, plan.Defend, plan.Places) = (false, false, false, false, 0); // (phase 3's missions round the person off: the far layer alone)
        var ctl = new PowerControllers { Rules = rules, Territory = t };
        ctl.Reset();
        rel.Reset();
        var host = new Host(w, crew, place) { Ctl = ctl, World = "check" };
        host.Persons.Add(new Vector3(0, 0, 3000)); // (9 km from the site: nothing is set down)
        rel.Sight = host;
        rel.Move(raiders, militia, -70 - rel.Get(raiders, militia)); // (at war both ways)
        rel.Move(militia, raiders, -70 - rel.Get(militia, raiders));
        var p = ctl.Powers[raiders];
        var gi = ctl.FarForm(rel, p, raiders, FarTask.Take, 0, 3, ShipRole.Raider);
        if (gi < 0)
        {
            Console.WriteLine("fleet far: FAILED: no group formed");
            return false;
        }
        var people = new[] { host.Persons[0] };
        bool farQuiet = true, materialised = false, collapsed = false;
        var maxBodies = 0;
        var phase = 0;
        long walkedAt = 0, leftAt = 0;
        float hullsKept = -1;
        for (var tick = 0; tick < seconds * World.TickRate; tick++)
        {
            using (place.In())
            {
                crew.Step(w, q, null, people, crewOn: false);
                ctl.Step(crew, w, rel, host);
                rel.Step();
                w.Step(q, World.TickDt);
            }
            ref var g = ref ctl.Far[gi];
            var bodies = ctl.Bodies();
            maxBodies = Math.Max(maxBodies, bodies);
            if (phase == 0 && g.State is FarState.Land or FarState.AtSite && tick > 20 * World.TickRate)
            {
                farQuiet = host.Spawns == 0;
                Console.WriteLine($"  {tick / World.TickRate,3} s: the group is {g.State} on the tool's world, {Vector3.Distance(g.Pos, host.Persons[0]):0} m from the person; bodies {bodies}; the person walks to 3 km from it");
                host.Persons[0] = g.Pos + Vector3.Normalize(host.Persons[0] - g.Pos) * 3000;
                people[0] = host.Persons[0];
                (phase, walkedAt) = (1, tick);
            }
            else if (phase == 1 && g.Body == 2)
            {
                materialised = true;
                Console.WriteLine($"  {tick / World.TickRate,3} s: materialised: {bodies} bodies in crew group {g.RealNumber}, {(tick - walkedAt) / (float)World.TickRate:0.0} s after the person came near; the nearest set down {host.NearestSpawn:0} m from the person");
                (phase, walkedAt) = (2, tick);
            }
            else if (phase == 2 && tick - walkedAt > 15 * World.TickRate)
            {
                Console.WriteLine($"  {tick / World.TickRate,3} s: the person walks away, to 10 km from the group");
                host.Persons[0] = g.Pos + Vector3.Normalize(host.Persons[0] - g.Pos) * 10000;
                people[0] = host.Persons[0];
                (phase, leftAt) = (3, tick);
            }
            else if (phase == 3 && g.Body == 0 && ctl.Far.All(x => x.Body == 0) && bodies == 0)
            {
                collapsed = true;
                hullsKept = 0;
                foreach (var s in p.Ships)
                    if (s.Far == gi + 1 && s.State == PowerShipState.Far)
                        hullsKept += s.Hull;
                Console.WriteLine($"  {tick / World.TickRate,3} s: collapsed back to a record {(tick - leftAt) / (float)World.TickRate:0.0} s after the person left; {g.Ships} ships, hulls {hullsKept:0.00} in all; {host.Docks} bodies taken out");
                phase = 4;
            }
            foreach (var e in crew.Events)
                if (e.StartsWith("powers:", StringComparison.Ordinal))
                    Console.WriteLine($"  {tick / World.TickRate,3} s [crew] {e}");
            crew.Events.Clear();
            foreach (var n in ctl.News)
                Console.WriteLine($"  {tick / World.TickRate,3} s [news] {n}");
            ctl.News.Clear();
            foreach (var l in ctl.Log)
                Console.WriteLine($"  {tick / World.TickRate,3} s [powers] {l}");
            ctl.Log.Clear();
            if (phase == 4)
                break;
        }
        Console.Write(ctl.TerritoryText());
        var ok = farQuiet && materialised && collapsed && host.NearestSpawn >= t.SpawnFromPeople - 1;
        Console.WriteLine($"fleet far: nothing set down far off {(farQuiet ? "yes" : "NO")}; materialised out of sight {(materialised && host.NearestSpawn >= t.SpawnFromPeople - 1 ? "yes" : "NO")} (at most {maxBodies} bodies); collapsed {(collapsed ? "yes" : "NO")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
