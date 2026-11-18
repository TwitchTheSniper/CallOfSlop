using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-fleet strike|screen|succession|convoy|mining|interrogate|scale|all [--seconds S] [--fleet-scale N] [--fleet-awake K]
/// [--group-size G] [--crew-scale-ship NAME]</c> (docs/sandbox/design/faction-controllers.md phase 2): groups led by admirals, driven by their
/// seated crews with no game (a tool's world: no bodies). Each prints what happened and a verdict; exit 0 when every part asked passed.
/// </summary>
public static class CrewFleetCheck
{
    public static int Run(string which, string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 0;
        var ok = true;
        var all = which.Equals("all", StringComparison.OrdinalIgnoreCase);
        bool Is(string n) => all || which.Equals(n, StringComparison.OrdinalIgnoreCase);
        if (Is("strike"))
            ok &= Strike(seconds > 0 ? seconds : 60);
        if (Is("screen"))
            ok &= Screen(seconds > 0 ? seconds : 40);
        if (Is("succession"))
            ok &= Succession("Militia") & Succession("Raiders");
        if (Is("convoy"))
            ok &= Convoy(seconds > 0 ? seconds : 70);
        if (Is("mining"))
            ok &= Mining(seconds > 0 ? seconds : 200);
        if (Is("interrogate"))
            ok &= Interrogate();
        if (which.Equals("scale", StringComparison.OrdinalIgnoreCase))
            ok &= Scale(int.TryParse(Arg("--fleet-scale"), out var n) ? n : 1000, int.TryParse(Arg("--fleet-awake"), out var k) ? k : 64,
                int.TryParse(Arg("--group-size"), out var gs) ? Math.Clamp(gs, 1, CrewGroup.MaxShips) : CrewGroup.MaxShips, Arg("--crew-scale-ship") ?? "Bandit Helicopter 1",
                seconds > 0 ? seconds : 5);
        Console.WriteLine($"fleet: {(ok ? "OK" : "FAILED")}");
        return ok ? 0 : 1;
    }

    private static ConstructBuild.Blueprint Bp(string name) => ConstructBuild.FindBlueprint(name) ?? throw new InvalidOperationException($"no blueprint \"{name}\"");

    private static void Flush(CrewWorld crew, bool all = false)
    {
        foreach (var e in crew.Events)
            if (all || e.StartsWith("fleet:", StringComparison.Ordinal) || e.Contains("its captain", StringComparison.Ordinal))
                Console.WriteLine($"  [crew] {e}");
        crew.Events.Clear();
        foreach (var n in crew.Fleet.News)
            Console.WriteLine($"  [news] {n}");
        crew.Fleet.News.Clear();
    }

    private static int Group(CrewWorld crew, ConstructWorld w, GroupTask task, IEnumerable<Construct> bodies, string why)
    {
        Span<ushort> ids = stackalloc ushort[CrewGroup.MaxShips];
        var n = 0;
        foreach (var b in bodies)
            ids[n++] = b.Id;
        return crew.Fleet.Form(crew, w, task, ids[..n], why);
    }

    private static void Tick(CrewWorld crew, ConstructWorld w, SelfTest.DriveWorld q, VehicleDriveCheck.CheckPlace place, Vector3[] people)
    {
        using (place.In())
        {
            crew.Step(w, q, null, people, crewOn: false);
            w.Step(q, World.TickDt);
        }
    }

    /// <summary>Three crewed Zero Days (Militia) against two frigates, one shot about: each captain's own target is its nearest; the admiral focuses all three on the one worth most.</summary>
    private static bool Strike(int seconds)
    {
        Console.WriteLine("fleet strike: three Militia Zero Days under an admiral against two frigates (the far one shot about)");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName("Militia");
        var bp = Bp("Zero Day Frigate");
        var ships = new List<Construct>();
        for (var i = 0; i < 3; i++)
        {
            var c = CrewCheck.Build(w, bp, new Vector3((i - 1) * 90, 0, 0), 0, place);
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            ships.Add(c);
        }
        var near = CrewCheck.Build(w, bp, new Vector3(-250, 0, -1100), 0, place);
        var far = CrewCheck.Build(w, bp, new Vector3(600, 0, -900), 0, place); // (the starboard ship's nearest: two contacts)
        for (var i = 0; i < far.PartCount; i++)
            if (far.Parts[i].D.Health > 0)
                far.Parts[i].Health *= 0.35f; // (its hull shot about: the best target for a skilled admiral)
        var hpNear0 = CrewCheck.Health(near);
        var hpFar0 = CrewCheck.Health(far);
        crew.FixedFor = (Construct c, CrewShip cs, out ShipAiTarget t) =>
        {
            var tc = Vector3.DistanceSquared(c.WorldCom, near.WorldCom) < Vector3.DistanceSquared(c.WorldCom, far.WorldCom) ? near : far;
            t = new ShipAiTarget { At = tc.WorldCom, Vel = tc.Vel, Body = tc.Id, Player = 255 };
            return true;
        };
        var slot = Group(crew, w, GroupTask.Strike, ships, "a strike");
        Flush(crew);
        var people = new[] { Vector3.Zero };
        var onFocus = 0;
        var looks = 0;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            people[0] = ships[0].WorldCom;
            Tick(crew, w, q, place, people);
            Flush(crew);
            if (t % (5 * World.TickRate) == 5 * World.TickRate - 1 && crew.Fleet.Of(slot) is { } g)
            {
                var orders = string.Join(", ", ships.Select(c => crew.ShipOf(c.Id) is { } cs ? $"{cs.Order.Kind}{(cs.Order.Body != 0 ? $" {cs.Order.Body}" : "")} ({cs.Ai?.State ?? "no driver"})" : "gone"));
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: focus {g.Focus} (near {near.Id}, far {far.Id}); strength {g.Strength:0} against {g.Enemy:0}; orders: {orders}; near {CrewCheck.Health(near) / hpNear0 * 100:0}%, far {CrewCheck.Health(far) / hpFar0 * 100:0}%");
                if (g.Focus != 0)
                {
                    looks++;
                    if (ships.All(c => crew.ShipOf(c.Id) is not { } cs || cs.Order.Kind == OrderKind.Engage && cs.Order.Body == g.Focus))
                        onFocus++;
                }
            }
        }
        var g2 = crew.Fleet.Of(slot);
        var focused = looks > 0 && onFocus == looks && g2 != null && g2.Focus == far.Id;
        var hit = CrewCheck.Health(far) < hpFar0 * 0.95f;
        Console.WriteLine($"fleet strike: focus fire {onFocus} of {looks} looks (on the shot-about frigate: {(g2?.Focus == far.Id ? "yes" : "NO")}), far frigate {CrewCheck.Health(far) / hpFar0 * 100:0}% left: {(focused && hit ? "OK" : "FAILED")}; " +
                          $"admirals' cost {crew.Fleet.CpuMs / Math.Max(1, crew.Fleet.Ticks) * 1000:0.0} us a tick (worst {crew.Fleet.WorstMs * 1000:0} us), crew {crew.CpuMs / Math.Max(1, crew.Ticks) * 1000:0} us, {crew.Bytes} bytes");
        return focused && hit;
    }

    /// <summary>A civilian group (a Traders convoy of three Zero Days) on its way, a threat off its beam: the flagship keeps going, the two armed escorts screen between.</summary>
    private static bool Screen(int seconds)
    {
        Console.WriteLine("fleet screen: a Traders convoy (three Zero Days) on its way east, a hostile frigate to its north");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName("Traders");
        var bp = Bp("Zero Day Frigate");
        var ships = new List<Construct>();
        for (var i = 0; i < 3; i++)
        {
            var c = CrewCheck.Build(w, bp, new Vector3(0, 0, i * 80), MathF.PI * 0.5f, place);
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            ships.Add(c);
        }
        var threat = CrewCheck.Build(w, bp, new Vector3(400, 0, -900), 0, place);
        crew.Fixed = new ShipAiTarget { At = threat.WorldCom, Vel = Vector3.Zero, Body = threat.Id, Player = 255 };
        var slot = Group(crew, w, GroupTask.Convoy, ships, "a trade run");
        crew.Fleet.Groups[slot].Doctrine = new FleetDoctrine { Name = "check", Caution = 0.2f, Formation = FleetFormation.Column, Spacing = 80 }; // (bold: it screens rather than runs)
        crew.Fleet.Command(slot, OrderKind.GoTo, new Vector3(3000, 60, 0));
        Flush(crew);
        var people = new[] { Vector3.Zero };
        var between = 0;
        var looks = 0;
        var flag0 = crew.Fleet.Groups[slot].Flagship;
        var flagStart = w.Get(flag0)!.WorldCom;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            crew.Fixed = new ShipAiTarget { At = threat.WorldCom, Vel = Vector3.Zero, Body = threat.Id, Player = 255 };
            people[0] = ships[0].WorldCom;
            Tick(crew, w, q, place, people);
            Flush(crew);
            if (t % (5 * World.TickRate) == 5 * World.TickRate - 1 && crew.Fleet.Of(slot) is { } g && w.Get(g.Flagship) is { } flag)
            {
                var toThreat = Vector3.Normalize((threat.WorldCom - flag.WorldCom) with { Y = 0 });
                var line = new List<string>();
                foreach (var c in ships)
                {
                    if (crew.ShipOf(c.Id) is not { } cs || c.Id == g.Flagship)
                        continue;
                    var off = (c.WorldCom - flag.WorldCom) with { Y = 0 };
                    var dot = off.LengthSquared() > 1 ? Vector3.Dot(Vector3.Normalize(off), toThreat) : 0;
                    line.Add($"{c.Id} {cs.Order.Kind} toward the threat {dot:0.00} ({cs.Ai?.State})");
                    if (t > 20 * World.TickRate)
                    {
                        looks++;
                        if (cs.Order.Kind == OrderKind.Screen && dot > 0.3f)
                            between++;
                    }
                }
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: flagship {flag.Id} {crew.ShipOf(flag.Id)?.Order.Kind} {crew.ShipOf(flag.Id)?.Ai?.State}; {string.Join("; ", line)}");
            }
        }
        var moved = crew.Fleet.Of(slot) is { } g3 && w.Get(g3.Flagship) is { } f3 && Vector3.Distance(f3.WorldCom with { Y = 0 }, flagStart with { Y = 0 }) > 100;
        var ok = looks > 0 && between * 2 >= looks && moved;
        Console.WriteLine($"fleet screen: escorts screening between the flagship and the threat {between} of {looks} looks; the flagship kept on its way: {(moved ? "yes" : "NO")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>The admiral killed: a Militia group's senior captain steps up after its doctrine's delay; a Raiders group scatters.</summary>
    private static bool Succession(string kind)
    {
        Console.WriteLine($"fleet succession: three {kind} helicopters under an admiral, the admiral shot at 10 s");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName(kind);
        var bp = Bp("Bandit Helicopter 1");
        var ships = new List<Construct>();
        for (var i = 0; i < 3; i++)
        {
            var c = CrewCheck.Build(w, bp, new Vector3(i * 60, 0, 0), 0, place);
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            ships.Add(c);
        }
        var slot = Group(crew, w, GroupTask.Patrol, ships, "a patrol");
        var g = crew.Fleet.Groups[slot];
        var doctrine = g.Doctrine;
        crew.Fleet.Command(slot, OrderKind.GoTo, new Vector3(0, 60, -1500));
        Flush(crew);
        var people = new[] { Vector3.Zero };
        var oldUid = g.AdmiralUid;
        long killedAt = -1, steppedAt = -1, brokeAt = -1;
        for (var t = 0; t < 40 * World.TickRate; t++)
        {
            people[0] = ships[0].WorldCom;
            Tick(crew, w, q, place, people);
            Flush(crew);
            if (t == 10 * World.TickRate && crew.Fleet.AdmiralOf(crew, g) is var m and >= 0)
            {
                Console.WriteLine($"  10 s: Admiral {CrewNames.Of(crew.Name[m])} (aboard construct {g.Flagship}) is shot");
                crew.Kill(m, null);
                killedAt = t;
            }
            if (killedAt >= 0 && steppedAt < 0 && g.Live && g.AdmiralUid != 0 && g.AdmiralUid != oldUid)
                steppedAt = t;
            if (killedAt >= 0 && brokeAt < 0 && !g.Live)
                brokeAt = t;
        }
        var running = ships.Count(c => crew.ShipOf(c.Id) is { Order.Kind: OrderKind.Retreat });
        bool ok;
        if (doctrine.Succession == FleetSuccession.Break)
        {
            ok = brokeAt >= 0;
            Console.WriteLine($"fleet succession ({kind}, break): the group {(brokeAt >= 0 ? $"broke {(brokeAt - killedAt) / (float)World.TickRate:0.0} s after" : "DIDN'T BREAK")}; {running} ships ran: {(ok ? "OK" : "FAILED")}");
        }
        else
        {
            var delay = (steppedAt - killedAt) / (float)World.TickRate;
            ok = steppedAt >= 0 && delay >= doctrine.Delay - 0.1f;
            Console.WriteLine($"fleet succession ({kind}, {doctrine.Succession}, delay {doctrine.Delay:0} s): {(steppedAt >= 0 ? $"Admiral {CrewNames.Of(g.AdmiralName)} in command {delay:0.0} s after" : "NOBODY STEPPED UP")}: {(ok ? "OK" : "FAILED")}");
        }
        return ok;
    }

    /// <summary>A spawner-style convoy (four Bandit ground vehicles, Militia) under an admiral: sent 300 m out, then a strong frigate appears and the admiral weighs it.</summary>
    private static bool Convoy(int seconds)
    {
        Console.WriteLine("fleet convoy: four Militia ground vehicles as a convoy under an admiral; sent off at 5 s; a hostile Zero Day near at 45 s");
        var names = new[] { "Bandit Gun Wagon 1", "Bandit Buggy 1", "Bandit Car 1", "Bandit Monster Truck 1" };
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName("Militia");
        var bodies = new List<Construct>();
        var back = 0f;
        foreach (var name in names)
        {
            var c = CrewCheck.Build(w, Bp(name), new Vector3(0, 0, back), 0, place);
            var brief = VehicleBriefMake.Rebuild(c, name);
            brief.Convoy = 1;
            brief.ConvoyIndex = (byte)bodies.Count;
            brief.Leader = bodies.Count > 0 ? bodies[0].Id : (ushort)0;
            brief.Attach(c);
            back += c.LocalMax.Z - c.LocalMin.Z + VehicleConvoys.Gap + 4;
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            bodies.Add(c);
        }
        var slot = Group(crew, w, GroupTask.Convoy, bodies, "a convoy");
        var g = crew.Fleet.Groups[slot];
        var leaderWasFlag = g.Flagship == bodies[0].Id;
        Flush(crew);
        Construct? threat = null;
        var people = new[] { Vector3.Zero };
        var goal = new Vector3(0, 0, -300);
        float gapSum = 0;
        var gapN = 0;
        var retreated = false;
        var arrived = false;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            if (t == 5 * World.TickRate)
            {
                crew.Fleet.Command(slot, OrderKind.GoTo, goal);
                Console.WriteLine($"  5 s: sent to {goal}");
            }
            if (t == 45 * World.TickRate)
            {
                arrived = Vector3.Distance(bodies[0].WorldCom with { Y = 0 }, goal) < 80;
                using (place.In())
                    threat = CrewCheck.Build(w, Bp("Zero Day Frigate"), (w.Get(g.Flagship)?.WorldCom ?? Vector3.Zero) + new Vector3(220, 0, 0), 0, place);
                Console.WriteLine($"  45 s: a hostile Zero Day ({threat.PartCount} parts) 220 m off");
            }
            crew.Fixed = threat != null ? new ShipAiTarget { At = threat.WorldCom, Vel = threat.Vel, Body = threat.Id, Player = 255 } : null;
            people[0] = bodies[0].WorldCom;
            Tick(crew, w, q, place, people);
            Flush(crew);
            if (t > 15 * World.TickRate && t < 45 * World.TickRate)
                for (var k = 2; k < bodies.Count; k++) // (as phase 2's convoy check: the Bandit Buggy second in line is slow to its place)
                    if (crew.ShipOf(bodies[k].Id)?.Ai is { State: var st } && (st.StartsWith("following", StringComparison.Ordinal) || st.StartsWith("ordered: form", StringComparison.Ordinal)))
                    {
                        gapSum += MathF.Abs(float.Parse(st.Split(' ')[st.StartsWith("following", StringComparison.Ordinal) ? 1 : 2].TrimEnd(','), System.Globalization.CultureInfo.InvariantCulture));
                        gapN++;
                    }
            retreated |= g.Live && g.Retreating;
            if (t % (5 * World.TickRate) == 5 * World.TickRate - 1)
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: {string.Join("; ", bodies.Select(b => $"{b.Id}: {crew.ShipOf(b.Id)?.Order.Kind} {b.Vel.Length():0.0} m/s {crew.ShipOf(b.Id)?.Ai?.State ?? "no driver"}"))}");
        }
        var kept = gapN > 0 && gapSum / gapN < 20;
        var ok = leaderWasFlag && kept && retreated && arrived;
        Console.WriteLine($"fleet convoy: the leader is the flagship: {(leaderWasFlag ? "yes" : "NO")}; followers {(gapN > 0 ? gapSum / gapN : 0):0.0} m off their places on the way: {(kept ? "kept" : "LOST")}; " +
                          $"the leader {(arrived ? "reached" : "was short of")} the goal before the threat; against the frigate the admiral {(retreated ? "pulled back" : "DIDN'T PULL BACK")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>A civilian mining group (three Traders helicopters): mine at one place 10 s, deliver at another 10 s, round again.</summary>
    private static bool Mining(int seconds)
    {
        Console.WriteLine("fleet mining: three Traders Zero Days on a route: mine 900 m north (10 s), deliver at home (10 s), again (helicopters fly poorly under the crews' flying: their noses wander, as in --crew-check \"Bandit Helicopter 1\")");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName("Traders");
        var ships = new List<Construct>();
        for (var i = 0; i < 3; i++)
        {
            var c = CrewCheck.Build(w, Bp("Zero Day Frigate"), new Vector3(i * 90, 0, 0), 0, place);
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            ships.Add(c);
        }
        var slot = Group(crew, w, GroupTask.Mining, ships, "a mining run");
        crew.Fleet.SetRoute(slot, [new Vector3(0, 0, -900), new Vector3(0, 0, 0)], [OrderKind.Mine, OrderKind.Deliver], 10);
        Flush(crew);
        var people = new[] { Vector3.Zero };
        var arrivals = new List<int>();
        var g = crew.Fleet.Groups[slot];
        var seen = 0;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            people[0] = ships[0].WorldCom;
            Tick(crew, w, q, place, people);
            Flush(crew);
            for (var k = 0; k < g.RingCount; k++)
            {
                var e = g.Ring[(g.RingHead - g.RingCount + k + CrewGroup.RingSize) % CrewGroup.RingSize];
                if (e.Kind == FleetReport.Arrived && e.Tick > seen)
                {
                    arrivals.Add(e.Other);
                    seen = (int)e.Tick;
                }
            }
            if (t % (10 * World.TickRate) == 10 * World.TickRate - 1)
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: {string.Join("; ", ships.Select(b => $"{b.Id}: {crew.ShipOf(b.Id)?.Ai?.State ?? "no driver"} at {b.Vel.Length():0} m/s, {b.WorldCom.Y:0} m up, helm {(crew.ShipOf(b.Id) is { } hs && hs.HelmOn(crew) ? "manned" : "EMPTY")}, keys {(crew.ShipOf(b.Id) is { Helm: >= 0 } ks ? b.Parts[ks.StPart[ks.Helm]].Keys : 0):X}, nose to its way {Vector3.Dot(Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, b.Rot) with { Y = 0 }), b.Vel.LengthSquared() > 1 ? Vector3.Normalize(b.Vel with { Y = 0 }) : Vector3.Zero):0.0}"))}");
        }
        var looped = crew.ShipOf(g.Flagship) is { Order.Kind: OrderKind.Mine } && arrivals.Count >= 2;
        var ok = arrivals.Count >= 2 && arrivals[0] == 0 && arrivals[1] == 1 && looped;
        Console.WriteLine($"fleet mining: route points reached in order: {string.Join(", ", arrivals.Select(a => a == 0 ? "mine" : "deliver"))}{(looped ? ", then back to mine" : "")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>An admiral taken prisoner and questioned (the intelligence core: what each officer gives up, and how long it takes).</summary>
    private static bool Interrogate()
    {
        Console.WriteLine("fleet interrogate: a Militia group of three and another of two; the first's admiral taken prisoner, then a captain");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var power = CrewPowers.ForKindName("Militia");
        var a = new List<Construct>();
        var b = new List<Construct>();
        for (var i = 0; i < 5; i++)
        {
            var c = CrewCheck.Build(w, Bp("Bandit Helicopter 1"), new Vector3(i * 60 + (i >= 3 ? 2000 : 0), 0, 0), 0, place);
            crew.Crew(c, ShipAiMode.Everyone, faction: power);
            (i < 3 ? a : b).Add(c);
        }
        var sa = Group(crew, w, GroupTask.Patrol, a, "a patrol");
        var sb = Group(crew, w, GroupTask.Defend, b, "a defence");
        var g = crew.Fleet.Groups[sa];
        var people = new[] { Vector3.Zero };
        for (var t = 0; t < 3 * World.TickRate; t++)
            Tick(crew, w, q, place, people);
        Flush(crew);
        var m = crew.Fleet.AdmiralOf(crew, g);
        if (m < 0)
        {
            Console.WriteLine("fleet interrogate: NO ADMIRAL");
            return false;
        }
        var uid = crew.Uid[m];
        var serial = g.Serial;
        crew.State[m] = CrewState.Surrendered; // (taken: its ship's crew gave up)
        var flagShip = crew.ShipOf(g.Flagship)!;
        flagShip.Prize = true;
        for (var t = 0; t < 12 * World.TickRate; t++)
            Tick(crew, w, q, place, people);
        Flush(crew);
        var r = FleetRules.Current;
        var res = CrewFleet.ResistanceOf(uid, g.Doctrine);
        Console.WriteLine($"  Admiral {CrewNames.Of(crew.Name[m])}: resistance {res:0.00}: {crew.Fleet.InterrogationSeconds(res, 200):0} s with a skilled interrogator (200), {crew.Fleet.InterrogationSeconds(res, 60):0} s an unskilled one, {crew.Fleet.InterrogationSeconds(res, -1):0} s alone; ransom {r.RansomAdmiral} credits");
        var lines = new List<string>();
        var marks = new List<(Vector3, string)>();
        crew.Fleet.Intel(crew, w, null, uid, power, (short)sa, serial, flagShip.Body, true, lines, marks);
        foreach (var l in lines)
            Console.WriteLine($"  admiral says: {l}");
        Console.WriteLine($"  marks: {string.Join("; ", marks.Select(x => $"{x.Item2} at {x.Item1.X:0},{x.Item1.Z:0}"))}");
        var adOk = marks.Count >= 3 && lines.Any(l => l.Contains("Admiral", StringComparison.Ordinal));
        var other = crew.ShipOf(b[0].Id)!;
        var cap = CrewFleet.CaptainOf(crew, other);
        lines.Clear();
        marks.Clear();
        crew.Fleet.Intel(crew, w, null, crew.Uid[cap], power, other.Group, crew.Fleet.Groups[sb].Serial, other.Body, false, lines, marks);
        foreach (var l in lines)
            Console.WriteLine($"  Captain {CrewNames.Of(crew.Name[cap])} says: {l}");
        var capOk = marks.Count >= 1 && lines.Count >= 2;
        var ok = adOk && capOk;
        Console.WriteLine($"fleet interrogate: the admiral gave up {(adOk ? "its group and the other admiral" : "TOO LITTLE")}, the captain {(capOk ? "its ship and crew" : "TOO LITTLE")}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>
    /// The scale target (1,000 faction ships as bodies near players): <paramref name="n"/> copies of a craft in groups, <paramref name="awake"/> of
    /// them driven near the person (the driver cap raised to match), the rest far. The admirals', the crews' and the world's own step timed.
    /// </summary>
    private static bool Scale(int n, int awake, int groupSize, string craft, int seconds)
    {
        var bp = Bp(craft);
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        CrewWorld.DriverCap = Math.Max(CrewWorld.MaxDrivers, awake);
        var power = CrewPowers.ForKindName("Militia");
        var build0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var bodies = new List<Construct>(n);
        var side = (int)MathF.Ceiling(MathF.Sqrt(Math.Max(1, awake)));
        for (var i = 0; i < n; i++)
        {
            // The awake ones on a grid 70 m apart round the person (within 3 km), the rest 8 km out on a ring (asleep).
            var at = i < awake ? new Vector3((i % side - side / 2) * 70, 0, (i / side - side / 2) * 70) : new Vector3(MathF.Cos(i * 0.01f) * 8000, 0, MathF.Sin(i * 0.01f) * 8000);
            using (place.In())
            {
                var c = CrewCheck.Build(w, bp, at, 0, place);
                crew.Crew(c, ShipAiMode.Everyone, faction: power);
                bodies.Add(c);
            }
        }
        var buildS = (System.Diagnostics.Stopwatch.GetTimestamp() - build0) / (double)System.Diagnostics.Stopwatch.Frequency;
        for (var i = 0; i < bodies.Count; i += groupSize)
            Group(crew, w, GroupTask.Patrol, bodies.Skip(i).Take(groupSize), "scale");
        crew.Events.Clear();
        Console.WriteLine($"fleet scale: {n} {bp.Name} ({bodies[0].PartCount} parts each) built in {buildS:0} s, {crew.Fleet.Live} groups of up to {groupSize}, {awake} near the person; driver cap {CrewWorld.DriverCap}");
        // Each awake group goes somewhere: the admirals' orders drive their ships.
        for (var g = 0; g < CrewFleet.MaxGroups; g++)
            if (crew.Fleet.Of(g) is { } grp && w.Get(grp.Flagship) is { } f && f.WorldCom.Length() < 4000)
                crew.Fleet.Command(g, OrderKind.GoTo, f.WorldCom + new Vector3(0, 80, -800));
        var people = new[] { Vector3.Zero };
        crew.Fixed = null;
        for (var t = 0; t < 3 * World.TickRate; t++) // (warm up: the drivers taken, every group thought once)
            Tick(crew, w, q, place, people);
        crew.Events.Clear();
        crew.Fleet.News.Clear();
        crew.CpuMs = crew.WorstMs = 0;
        crew.Bytes = 0;
        crew.Ticks = 0;
        crew.Fleet.CpuMs = crew.Fleet.WorstMs = 0;
        crew.Fleet.Ticks = crew.Fleet.Thinks = 0;
        double worldMs = 0, worstWorld = 0;
        var ticks = seconds * World.TickRate;
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < ticks; t++)
        {
            using (place.In())
            {
                crew.Step(w, q, null, people, crewOn: false);
                var w0 = System.Diagnostics.Stopwatch.GetTimestamp();
                w.Step(q, World.TickDt);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - w0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                worldMs += ms;
                worstWorld = Math.Max(worstWorld, ms);
            }
            crew.Events.Clear();
            crew.Fleet.News.Clear();
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        var driven = 0;
        var moving = 0;
        foreach (var c in bodies)
            if (crew.ShipOf(c.Id) is { Ai: not null })
            {
                driven++;
                if (c.Vel.Length() > 2)
                    moving++;
            }
        var fleetUs = crew.Fleet.CpuMs / Math.Max(1, crew.Fleet.Ticks) * 1000;
        var crewMs = crew.CpuMs / Math.Max(1, crew.Ticks);
        var worldAvg = worldMs / ticks;
        Console.WriteLine($"fleet scale: admirals {fleetUs:0.0} us a tick (worst {crew.Fleet.WorstMs * 1000:0} us, {crew.Fleet.Thinks} thinks in {ticks} ticks); crews {crewMs:0.00} ms a tick (worst {crew.WorstMs:0.00} ms, {crew.Bytes} bytes); " +
                          $"the world's step {worldAvg:0.00} ms a tick (worst {worstWorld:0.0} ms); {driven} driven, {moving} moving; all of it {crewMs + worldAvg:0.00} ms a tick against the 8 ms budget; {bytes} bytes allocated in all");
        CrewWorld.DriverCap = CrewWorld.MaxDrivers;
        var ok = fleetUs < 300 && crew.Bytes == 0;
        Console.WriteLine($"fleet scale: the admirals within 0.3 ms and allocating nothing: {(ok ? "OK" : "OVER")}; the whole tick {(crewMs + worldAvg < 8 ? "within" : "OVER")} 8 ms");
        return ok;
    }
}
