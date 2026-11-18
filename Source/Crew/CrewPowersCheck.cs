using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-fleet powers [--seconds S]</c> and <c>--crew-fleet politics</c> (docs/sandbox/design/faction-controllers.md phase 3): the faction
/// controllers in a tool's world with one person standing at its middle. <c>powers</c>: the Traders' controller sends an escorted convoy between its two
/// places, the Raiders' raids it, the Traders call for help and the Militia answers; every power's books are checked against its stock at each think
/// (spending never past what it has). The ships are each kind's placeholder vehicles (Crew/Controllers.json), bought at their real bills. Its numbers are
/// the file's with the waits and distances shortened so the whole story fits in a few minutes. <c>politics</c>: the wider scale and the new pacts on the
/// relations engine alone: a trade agreement, a blood feud, a tribute that becomes vassalage, two kindred powers merging.
/// </summary>
public static class CrewPowersCheck
{
    private sealed class Host(ConstructWorld w, CrewWorld crew, PowerControllers ctl0, VehicleDriveCheck.CheckPlace place) : IPowerHost, IPowerSight
    {
        public PowerControllers ctl = ctl0;
        public Vector3 Person;
        public int Spawns, Docks;
        public double BuildMs; // (the tool builds at once, inside the think: the game's spawner builds in slices of its own)

        public int People(Span<Vector3> into)
        {
            into[0] = Person;
            return 1;
        }

        public string WorldKey => "check";

        public bool Ground(Vector3 at, out Vector3 ground)
        {
            ground = at with { Y = 0 };
            return true;
        }

        public bool Spawn(byte power, int ship, ConstructBuild.Blueprint bp, Vector3 at, float yaw)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var c = CrewCheck.Build(w, bp, at, yaw, place);
            BuildMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            crew.Crew(c, ShipAiMode.Strangers, faction: power);
            Spawns++;
            ctl.Spawned(crew, power, ship, c, "");
            return true;
        }

        public void Dock(Construct c)
        {
            if (crew.ShipOf(c.Id) is { } s)
                crew.Disband(s, w, null, "docked at home");
            w.Remove(c);
            Docks++;
        }

        public bool Sees(byte power, Vector3 at)
        {
            var sight = CrewPowers.KindOf(power)?.Sight ?? 2500;
            for (var i = 0; i < crew.ShipHigh; i++)
                if (crew.Ships[i] is { } s && s.Faction == power && w.Get(s.Body) is { } c && Vector3.DistanceSquared(c.WorldCom, at) < sight * sight)
                    return true;
            return false;
        }
    }

    public static bool Run(string which, int seconds) => which.Equals("politics", StringComparison.OrdinalIgnoreCase) ? Politics() : Powers(seconds > 0 ? seconds : 300);

    private static bool Powers(int seconds)
    {
        Console.WriteLine("fleet powers: a Traders convoy between its two places near a person, a Raiders camp and a Militia post; the controllers run it all");
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var rel = new PowerRelations { Table = new FactionTable() };
        var ctl = new PowerControllers();
        var file = ControllerRules.Current;
        // The file's numbers, its waits and distances shortened (a few minutes for the whole story).
        var rules = ControllerRules.Load(Path.Combine(AppFolders.Data, "Crew", "Controllers.json"));
        (rules.ThinkSeconds, rules.PlaceNear, rules.PlaceFar, rules.SpawnFromPeople, rules.DockFromPeople, rules.GatherMinutes, rules.ConvoyEveryMinutes, rules.QuietSeconds, rules.DwellSeconds, rules.MinBuildSeconds, rules.BuildSecondsScale) =
            (1, 280, 380, 0, 0, 1.5f, 0.1f, 20, 3, 3, 0.005f);
        ctl.Rules = rules;
        ctl.Reset();
        rel.Reset();
        var host = new Host(w, crew, ctl, place);
        rel.Sight = host;
        rel.Might = ctl.Might;
        byte traders = CrewPowers.ForKindName("Traders"), raiders = CrewPowers.ForKindName("Raiders"), militia = CrewPowers.ForKindName("Militia");
        if (traders == 0 || raiders == 0 || militia == 0)
        {
            Console.WriteLine("fleet powers: FAILED: no Traders, Raiders or Militia power");
            return false;
        }
        foreach (var (p, name) in new[] { (traders, "Traders"), (raiders, "Raiders"), (militia, "Militia") })
        {
            var cheapest = ctl.Cheapest(p);
            Console.WriteLine($"  {name}: start {ctl.Powers[p].Stock:0}, cheapest ship {cheapest:0}; bills: {string.Join(", ", ctl.Blueprints.Select((b, i) => (b, i)).Where(x => ctl.Rules.PlanOf(p).Fleet.Any(f => f.Contains(x.b.Name))).Select(x => $"{x.b.Name} {ctl.Bills[x.i].Units:0} units, {ctl.Bills[x.i].Seconds:0} s ({ctl.Bills[x.i].Parts} parts)"))}");
        }
        // Each crew fights the nearest crewed body of a power its power fights, within its kind's sight.
        crew.FixedFor = (Construct c, CrewShip cs, out ShipAiTarget t) =>
        {
            t = default;
            var sight = CrewPowers.KindOf(cs.Faction)?.Sight ?? 2000;
            var best = sight * sight;
            Construct? to = null;
            for (var i = 0; i < crew.ShipHigh; i++)
                if (crew.Ships[i] is { } o && o != cs && o.Faction != 0 && rel.Fights(cs.Faction, o.Faction) && w.Get(o.Body) is { Wreck: false } oc
                    && Vector3.DistanceSquared(oc.WorldCom, c.WorldCom) is var d2 && d2 < best)
                    (best, to) = (d2, oc);
            if (to == null)
                return false;
            t = new ShipAiTarget { At = to.WorldCom, Vel = to.Vel, Body = to.Id, Player = 255 };
            return true;
        };
        var people = new[] { Vector3.Zero };
        bool convoyFormed = false, escorted = false, raidFormed = false, raidEngaged = false, called = false, answered = false, defendRan = false, booksOk = true, neverNegative = true;
        bool delivered = false, looted = false;
        var news = new List<string>();
        double worstCtl = 0;
        var thinkMs = new List<double>(4096);
        var worstAt = 0;
        for (var t = 0; t < seconds * World.TickRate; t++)
        {
            using (place.In())
            {
                crew.Step(w, q, null, people, crewOn: false);
                var b0 = host.BuildMs;
                var s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                ctl.Step(crew, w, rel, host);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency - (host.BuildMs - b0);
                if (t > 60 * World.TickRate) // (past the first minute: its JIT)
                {
                    if (ms > worstCtl)
                        (worstCtl, worstAt) = (ms, t / World.TickRate);
                    if (ms > 0.002)
                        thinkMs.Add(ms);
                }
                rel.Step();
                w.Step(q, World.TickDt);
            }
            foreach (var e in crew.Events)
                if (e.StartsWith("fleet: group", StringComparison.Ordinal) && e.Contains("formed", StringComparison.Ordinal) || e.StartsWith("powers:", StringComparison.Ordinal))
                    Console.WriteLine($"  {t / World.TickRate,3} s [crew] {e}");
            crew.Events.Clear();
            foreach (var l in ctl.Log)
                Console.WriteLine($"  {t / World.TickRate,3} s [powers] {l}");
            ctl.Log.Clear();
            foreach (var n in ctl.News.Concat(crew.Fleet.News))
            {
                Console.WriteLine($"  {t / World.TickRate,3} s [news] {n}");
                news.Add(n);
                delivered |= n.Contains("delivers", StringComparison.Ordinal);
                looted |= n.Contains("takes its cargo", StringComparison.Ordinal);
                called |= n.Contains("calls for help", StringComparison.Ordinal);
                answered |= n.Contains($"{CrewPowers.Of(militia)!.Name} answers", StringComparison.Ordinal);
            }
            for (var k = rel.NewsTotal - 1; k >= 0 && k >= rel.NewsTotal - 4; k--)
                if (rel.NewsLine(k) is { } line && !news.Contains(line))
                {
                    Console.WriteLine($"  {t / World.TickRate,3} s [relations] {line}");
                    news.Add(line);
                }
            ctl.News.Clear();
            crew.Fleet.News.Clear();
            foreach (var a in new[] { traders, raiders, militia })
            {
                var p = ctl.Powers[a];
                neverNegative &= p.Stock >= -0.001;
                booksOk &= Math.Abs(p.Books - p.Stock) < 0.01;
                foreach (var m in p.Missions)
                {
                    if (crew.Fleet.Of(m.Group) is not { } g || g.Number != m.GroupNumber || m.Stage != PowerMissionStage.Run)
                        continue;
                    if (a == traders && m.Kind == MissionKind.Convoy)
                    {
                        convoyFormed = true;
                        var armed = 0;
                        for (var k = 0; k < g.Count; k++)
                            if (crew.ShipOf(g.Ships[k]) is { } s && w.Get(g.Ships[k]) is { } c && s.Strength > CrewWorld.HullOf(c) * 0.01f + 0.5f)
                                armed++;
                        escorted |= armed >= 1 && g.Count >= 2;
                    }
                    if (a == raiders && m.Kind == MissionKind.Raid)
                    {
                        raidFormed = true;
                        for (var k = 0; k < g.Count; k++)
                            if (crew.ShipOf(g.Ships[k]) is { } s && crew.Tick - s.TargetTick < 2 * World.TickRate && crew.ShipOf(s.TargetBody) is { } ts && ts.Faction == traders)
                                raidEngaged = true;
                    }
                    if (a == militia && m.Kind == MissionKind.Defend)
                        defendRan = true;
                }
            }
            if (t % (20 * World.TickRate) == 20 * World.TickRate - 1)
                Console.Write($"  {(t + 1) / World.TickRate,3} s:\n{ctl.Text(crew)}");
        }
        Console.Write(ctl.Text(crew));
        thinkMs.Sort();
        if (thinkMs.Count > 0)
            Console.WriteLine($"  thinks past the first minute: {thinkMs.Count}, median {thinkMs[thinkMs.Count / 2] * 1000:0} us, 99th {thinkMs[thinkMs.Count * 99 / 100] * 1000:0} us, worst {thinkMs[^1] * 1000:0} us at {worstAt} s; {thinkMs.Count(x => x > 0.5)} over 0.5 ms");
        Console.WriteLine($"  relations: Raiders see Traders {rel.Get(raiders, traders)}, Traders see Raiders {rel.Get(traders, raiders)}, Traders see Militia {rel.Get(traders, militia)}, Militia see Traders {rel.Get(militia, traders)}");
        var saved = ctl.SaveBytes();
        var back = new PowerControllers { Rules = rules };
        back.Load(new PowerRelations(), saved);
        var roundTrip = back.SaveBytes().AsSpan().SequenceEqual(saved) && back.Powers[traders].Stock == ctl.Powers[traders].Stock && back.Powers[raiders].ShipCount() == ctl.Powers[raiders].ShipCount();
        Console.WriteLine($"  the controllers section: {saved.Length} bytes, reads back {(roundTrip ? "the same" : "DIFFERENT")}");
        // A load: the ships out take up their own bodies again by their crews' uids (two a think), and no other.
        var load = new PowerControllers { Rules = rules };
        load.Load(rel, saved);
        host.ctl = load;
        rel.Might = load.Might;
        for (var t = 0; t < 10 * World.TickRate; t++)
            using (place.In())
                load.Step(crew, w, rel, host);
        var adopted = true;
        var outs = 0;
        foreach (var a in new[] { traders, raiders, militia })
            foreach (var sh in ctl.Powers[a].Ships)
                if (sh.State == PowerShipState.Out && sh.Body != 0)
                {
                    outs++;
                    var found = Array.Exists(load.Powers[a].Ships, x => x.State == PowerShipState.Out && x.Body == sh.Body || x.State == PowerShipState.Hangar && x.Uid == sh.Uid); // (or docked since)
                    if (!found)
                    {
                        var cs = crew.ShipOf(sh.Body);
                        var uids = cs != null ? string.Join(",", Enumerable.Range(0, cs.MemberCount).Select(j => crew.Uid[cs.Members[j]])) : "none";
                        var recs = string.Join(" ", load.Powers[a].Ships.Where(x => x.State != PowerShipState.None).Select(x => $"{x.State}/{x.Body}/{x.Uid}/{x.Mission}"));
                        Console.WriteLine($"  not taken up: {CrewPowers.Of(a)!.Name}'s construct {sh.Body} (uid {sh.Uid}, mission {sh.Mission}, crew group {cs?.Group}, its uids {uids}); load's records: {recs}");
                    }
                    adopted &= found;
                }
        Console.WriteLine($"  after a load: {outs} ships out taken up again by their crews: {(adopted ? "each its own body (or docked since)" : "NOT ALL")}");
        foreach (var l in load.Log)
            Console.WriteLine($"  [powers] {l}");
        roundTrip &= adopted;
        var ok = convoyFormed && escorted && raidFormed && raidEngaged && called && answered && defendRan && booksOk && neverNegative && roundTrip;
        Console.WriteLine($"fleet powers: convoy formed {Yes(convoyFormed)}, escorted {Yes(escorted)}, delivered {Yes(delivered)}; raid sent {Yes(raidFormed)}, engaged the convoy {Yes(raidEngaged)}, looted {Yes(looted)}; " +
                          $"call for help {Yes(called)}, the Militia answered {Yes(answered)} and went {Yes(defendRan)}; books match stock {Yes(booksOk)}, never below 0 {Yes(neverNegative)}; " +
                          $"bodies set down {host.Spawns}, docked {host.Docks}; controllers {(ctl.CpuMs - host.BuildMs) / Math.Max(1, ctl.Ticks) * 1000:0.0} us a tick without the tool's builds (worst {worstCtl * 1000:0} us, {ctl.Thinks} thinks; builds {host.BuildMs:0} ms in all), {ctl.Bytes} bytes; " +
                          $"crew {crew.CpuMs / Math.Max(1, crew.Ticks) * 1000:0} us: {(ok ? "OK" : "FAILED")}");
        ControllerRules.Use(file);
        return ok;
    }

    private static string Yes(bool b) => b ? "yes" : "NO";

    /// <summary>The wider scale and the new pacts, on the relations engine alone (no bodies).</summary>
    private static bool Politics()
    {
        Console.WriteLine("fleet politics: trade, a blood feud, tribute and vassalage, a merge");
        var ok = true;
        void Check(bool c, string what)
        {
            Console.WriteLine($"  {(c ? "ok" : "FAILED")}: {what}");
            ok &= c;
        }
        byte traders = CrewPowers.ForKindName("Traders"), raiders = CrewPowers.ForKindName("Raiders"), militia = CrewPowers.ForKindName("Militia"), mercs = CrewPowers.ForKindName("Mercenaries");
        var might = new float[PowerRelations.N + 1];
        Array.Fill(might, 10);
        var rel = new PowerRelations { Table = new FactionTable(), Might = p => might[p] };
        byte mergedA = 0, mergedB = 0;
        rel.Merge = (a, b) =>
        {
            (mergedA, mergedB) = (a, b);
            rel.Fall(a, $"{CrewPowers.Of(a)!.Name} joins {CrewPowers.Of(b)!.Name}");
        };
        rel.Reset();
        void Minutes(double m, Action? each = null)
        {
            for (var k = 0; k < m * 60 * World.TickRate; k++)
            {
                rel.Step();
                if (each != null && k % (60 * World.TickRate) == 0)
                    each();
            }
        }
        void Keep(byte a, byte b, int v) => rel.Move(a, b, v - rel.Get(a, b));
        // Trade: Traders and Militia, neutral with a common enemy (Raiders): non-aggression first, then trade (Traders' greed meets the line).
        Minutes(1);
        Check(rel.PactOf(traders, militia) is PactKind.NonAggression or PactKind.Trade, $"Traders and Militia sign a pact over their common enemy ({rel.PactOf(traders, militia)})");
        Minutes(6);
        Check(rel.PactOf(traders, militia) == PactKind.Trade, $"... and it grows into a trade agreement ({rel.PactOf(traders, militia)})");
        // Blood: Raiders keep killing Traders' ships until the floor, then once more.
        for (var k = 0; k < 20 && !rel.Blood(traders, raiders); k++)
            rel.Post(RelEvent.DestroyShip, traders, raiders, 255, default);
        Check(rel.Blood(traders, raiders) && rel.Get(traders, raiders) == PowerRelations.BloodValue, $"ships destroyed at the floor: the Traders swear a blood feud on the Raiders ({rel.Get(traders, raiders)}, {rel.Rules.Words(rel.Get(traders, raiders))})");
        rel.Post(RelEvent.Release, traders, raiders, 255, default);
        Minutes(10);
        Check(rel.Get(traders, raiders) == PowerRelations.BloodValue && rel.PactOf(traders, raiders) == PactKind.None, "a blood feud never moves: no release, no decay, no ceasefire");
        // Tribute: the Mercenaries at war with the Militia and losing, much the weaker and not proud (the default 0.5 < 0.6): they pay for peace.
        Keep(mercs, militia, -90);
        Keep(militia, mercs, -90);
        might[mercs] = 2;
        for (var k = 0; k < 12; k++)
            rel.Post(RelEvent.KillCrew, mercs, militia, 255, default);
        Minutes(2);
        var pi = rel.PactAt(mercs, militia);
        Check(pi >= 0 && rel.PactK[pi] == PactKind.Tribute && rel.PactA[pi] == mercs, $"the weaker Mercenaries, beaten, pay the Militia tribute ({(pi >= 0 ? rel.PactK[pi] : PactKind.None)})");
        // Vassalage: held long and warmed to neutral.
        Minutes(rel.Rules.VassalMinutes + 1, () =>
        {
            Keep(mercs, militia, 50); // (warmed by the years of peace: neutral, not friends)
            Keep(militia, mercs, 40);
        });
        pi = rel.PactAt(mercs, militia);
        Check(pi >= 0 && rel.PactK[pi] == PactKind.Vassalage && rel.Helps(militia, mercs), $"an old tribute, the payer neutral: the Mercenaries become the Militia's vassals ({(pi >= 0 ? rel.PactK[pi] : PactKind.None)})");
        // Merge: Militia and Mercenaries kindred both ways for mergeMinutes: the weaker joins.
        Minutes(rel.Rules.MergeMinutes + 2, () =>
        {
            Keep(mercs, militia, 100);
            Keep(militia, mercs, 100);
        });
        Check(mergedA == mercs && mergedB == militia && rel.Gone[mercs], $"kindred both ways for {rel.Rules.MergeMinutes} minutes: the weaker Mercenaries join the Militia ({(mergedA != 0 ? CrewPowers.Of(mergedA)!.Name : "none")})");
        // -100 alone is no feud (worlds from before phase 3 clamped there): it moves.
        Keep(raiders, militia, -100);
        rel.Post(RelEvent.Release, raiders, militia, 255, default);
        Check(!rel.Blood(raiders, militia) && rel.Get(raiders, militia) > -100, $"a relation at -100 with no feud moves as any other ({rel.Get(raiders, militia)})");
        // Where the controllers don't run (Might unset: creative), no trade, tribute, blood or merging.
        var off = new PowerRelations { Table = new FactionTable() };
        off.Reset();
        for (var k = 0; k < 8 * 60 * World.TickRate; k++)
            off.Step();
        for (var k = 0; k < 20; k++)
            off.Post(RelEvent.DestroyShip, traders, raiders, 255, default);
        Check(off.PactOf(traders, militia) != PactKind.Trade && !off.Blood(traders, raiders), $"with no controllers: no trade ({off.PactOf(traders, militia)}) and no blood feud ({off.Get(traders, raiders)})");
        // A save keeps the blood feud and the new pacts.
        var back = new PowerRelations { Table = new FactionTable(), Might = rel.Might };
        back.Load(rel.SaveBytes());
        Check(back.Blood(traders, raiders) && back.PactOf(traders, militia) == PactKind.Trade, "a save round trip keeps the blood feud and the trade agreement");
        for (var k = Math.Max(0, rel.NewsTotal - 16); k < rel.NewsTotal; k++)
            Console.WriteLine($"  [news] {rel.NewsLine(k)}");
        Console.WriteLine($"fleet politics: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
