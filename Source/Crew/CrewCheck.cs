using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check [SHIP] [--target NAME] [--range M] [--seconds S] [--still] [--crew-scale N]</c> (was <c>--ship-ai-check</c>; docs/sandbox/design/bot-brain.md):
/// a crewed ship (default "Zero Day Frigate") flown by its seated crew (CrewWorld, Karl's ShipAi drivers behind the rule-1 gate) against a target ship
/// <c>--range</c> m north (1200; flown by script unless <c>--still</c>), on the flight check's flat floor, in a world of its own with no game (so no
/// bodies: every member is a record at its seat). 120 s by default, in acts: it fights; at 40 s its pilot is killed (the helm keeps its last input for
/// <see cref="CrewWorld.HelmGraceTicks"/>, then a spare walks over and takes it); at 70 s its gunners are killed (their guns fall silent); at 90 s
/// everyone left at the helm or spare is killed (the ship stops: it holds). A line every two seconds and a summary; exit 0 when each act did what it
/// should. <c>--crew-scale N</c> then times the crew alone with N members on many copies of a small crewed craft, most far from the people (asleep),
/// some near (driven): ms per tick and bytes. Allocates (a tool, never on a tick).
/// </summary>
public static class CrewCheck
{
    public static int Cli(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var at = Array.FindIndex(args, a => a.Equals("--crew-check", StringComparison.OrdinalIgnoreCase));
        var shipName = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "Zero Day Frigate";
        var targetName = Arg("--target") ?? "Zero Day Frigate";
        var range = float.TryParse(Arg("--range"), System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 1200;
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 120;
        var still = args.Any(a => a.Equals("--still", StringComparison.OrdinalIgnoreCase));
        var scale = int.TryParse(Arg("--crew-scale"), out var sc) ? sc : 0;
        var shipBp = ConstructBuild.FindBlueprint(shipName);
        var targetBp = ConstructBuild.FindBlueprint(targetName);
        if (shipBp == null || targetBp == null)
        {
            Console.WriteLine($"no blueprint named \"{(shipBp == null ? shipName : targetName)}\" among {ConstructBuild.Blueprints.Count} ({ConstructBuild.RefusedBlueprints.Count} refused: {string.Join("; ", ConstructBuild.RefusedBlueprints.Take(3))})");
            return 1;
        }
        _faction = Arg("--crew-faction") is { } cf ? CrewFactions.Find(cf) : (byte)0;
        if (args.Any(a => a.Equals("--crew-work", StringComparison.OrdinalIgnoreCase)))
            return Work(shipBp, _faction != 0 ? _faction : CrewFactions.Find("Traders")) ? 0 : 1;
        if (args.Any(a => a.Equals("--crew-walk", StringComparison.OrdinalIgnoreCase)))
            return Walks(shipBp, float.TryParse(Arg("--speed"), System.Globalization.CultureInfo.InvariantCulture, out var sp) ? sp : 0, float.TryParse(Arg("--turn"), System.Globalization.CultureInfo.InvariantCulture, out var tn) ? tn * MathF.PI / 180f : 0) ? 0 : 1;
        if (Arg("--crew-convoy") is { } convoy)
            return Convoy(convoy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), seconds > 0 && Arg("--seconds") != null ? seconds : 60) ? 0 : 1;
        _faction = Arg("--crew-faction") is { } fn ? CrewFactions.Find(fn) : (byte)0;
        if (Arg("--crew-faction") is { } fname && _faction == 0)
        {
            Console.WriteLine($"no faction \"{fname}\" in Crew/Factions");
            return 1;
        }
        var ok = seconds <= 0 || Fight(shipBp, targetBp, shipName, targetName, range, seconds, still);
        if (scale > 0)
            ok &= Scale(scale, Arg("--crew-scale-ship") ?? "Bandit Helicopter 1");
        return ok ? 0 : 1;
    }

    private static byte _faction;

    private static Construct Build(ConstructWorld w, ConstructBuild.Blueprint bp, Vector3 ground, float yaw, VehicleDriveCheck.CheckPlace place)
    {
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(1000))
        {
        }
        using (place.In())
            return job.Join(ground, yaw, out var why) ?? throw new InvalidOperationException($"{bp.Name} doesn't build: {why}");
    }

    private static bool Fight(ConstructBuild.Blueprint shipBp, ConstructBuild.Blueprint targetBp, string shipName, string targetName, float range, int seconds, bool still)
    {
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var ship = Build(w, shipBp, Vector3.Zero, MathF.PI, place); // (facing south, away from the target: it must turn to fight)
        var target = Build(w, targetBp, new Vector3(0, 0, -range), 0, place);
        var crew = new CrewWorld();
        var cs = crew.Crew(ship, ShipAiMode.Everyone, faction: _faction)!;
        var tSeat = target.StabCount > 0 ? ConstructFlight.SeatFor(target, target.Stabs[0].Part) : -1;
        var hp0 = Health(target);
        var shots0 = w.ShotsFired;
        var shells0 = w.ShellsFired;
        var closest = float.MaxValue;
        Console.WriteLine($"crew: {shipName} ({ship.PartCount} parts, {ship.Mass / 1000:0} t) against {targetName} at {range:0} m{(still ? ", standing" : ", flying")}");
        Console.WriteLine($"  {crew.Describe(cs)}");
        Flush(crew);
        var people = new Vector3[1];
        int pilotKilledAt = -1, helmRetakenAt = -1, gunnersKilledAt = -1, allKilledAt = -1;
        var gunShotsAtKill = 0;
        var gunShotsLate = 0;
        Vector3 stopAt = default;
        var driftAfterStop = 0f;
        var hpAtPilotKill = 0f;
        var ticks = seconds * World.TickRate;
        for (var t = 0; t < ticks; t++)
        {
            var sec = t / World.TickRate;
            using (place.In())
            {
                if (tSeat >= 0 && !still && target.PartCount > tSeat)
                {
                    // The target's pilot: up for five seconds, then sliding one way and then the other every eight, forward a little in turns.
                    var keys = (Buttons)0;
                    var roll = (ushort)0;
                    if (sec < 5)
                        keys = Buttons.Sprint;
                    else
                    {
                        keys = sec % 24 < 4 ? Buttons.Forward : 0;
                        roll = sec % 16 < 8 ? ConstructParts.KeyRollLeft : ConstructParts.KeyRollRight;
                    }
                    target.Parts[tSeat].Keys = (ushort)((ushort)keys | roll | CPart.Occupied);
                    ConstructEdits.DriveInput(target, keys, World.TickDt);
                }
                crew.Fixed = target.PartCount > 0 ? new ShipAiTarget { At = target.WorldCom, Vel = target.Vel, Body = target.Id, Player = 255 } : null;
                people[0] = target.WorldCom; // (a "person" there: the ship stays awake, a seated crew)
                crew.Step(w, q, null, people, crewOn: false);
                w.Step(q, World.TickDt);
            }
            Flush(crew);
            var ai = cs.Ai;
            // The acts.
            if (t == 40 * World.TickRate && cs.Helm >= 0 && cs.StMember[cs.Helm] is var pm and >= 0)
            {
                pilotKilledAt = t;
                hpAtPilotKill = Health(target);
                Console.WriteLine($"  40 s: the pilot (#{crew.Uid[pm]}) is shot");
                crew.Kill(pm, null);
            }
            if (pilotKilledAt >= 0 && helmRetakenAt < 0 && cs.HelmOn(crew))
            {
                helmRetakenAt = t;
                Console.WriteLine($"  {t / (float)World.TickRate:0.0} s: {crew.Describe(cs)}");
            }
            if (t == 70 * World.TickRate)
            {
                gunnersKilledAt = t;
                gunShotsAtKill = ai?.GunnerShots ?? 0;
                for (var k = 0; k < cs.StCount; k++)
                    if (cs.StKind[k] == CrewStationKind.Gun && cs.StMember[k] >= 0)
                        crew.Kill(cs.StMember[k], null);
                Console.WriteLine($"  70 s: every gunner is shot; {crew.Describe(cs)}");
            }
            if (t == 71 * World.TickRate)
                gunShotsLate = ai?.GunnerShots ?? 0; // (a second on: no spare is in a gun seat yet, the walk takes two at least)
            if (t == 90 * World.TickRate)
            {
                allKilledAt = t;
                for (var i = cs.MemberCount - 1; i >= 0; i--)
                    crew.Kill(cs.Members[i], null);
                Console.WriteLine($"  90 s: everyone left is shot: {crew.Describe(cs)}");
            }
            if (t == ticks - 10 * World.TickRate)
                stopAt = ship.WorldCom; // (the last ten seconds: with nobody aboard it holds where its stabilizer leaves it)
            if (allKilledAt >= 0 && t > ticks - 10 * World.TickRate)
                driftAfterStop = Vector3.Distance(stopAt, ship.WorldCom);
            var d = Vector3.Distance(ship.WorldCom, target.WorldCom);
            closest = MathF.Min(closest, d);
            if (t % (2 * World.TickRate) == 2 * World.TickRate - 1)
            {
                var fwd = Vector3.Transform(-Vector3.UnitZ, ship.Rot);
                var to = Vector3.Normalize(target.WorldCom - ship.WorldCom);
                var face = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fwd with { Y = 0 }), Vector3.Normalize(to with { Y = 0 })), -1, 1)) * 180 / MathF.PI;
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: range {d,5:0} m, height {ship.WorldCom.Y,4:0} m (target {target.WorldCom.Y:0}), speed {ship.Vel.Length(),3:0} m/s, " +
                                  $"{face,3:0} deg off its nose, {(cs.HelmOn(crew) ? ai?.State ?? "no driver" : cs.InGrace(crew.Tick) ? "helm empty: last input held" : "helm empty")}; shots {w.ShotsFired - shots0} (gunners {ai?.GunnerShots ?? 0}), " +
                                  $"launches {ai?.Launches ?? 0} ({w.ShellsFired - shells0} shells), flares {ai?.Flares ?? 0}, target health {Health(target) / hp0 * 100:0}%, crew {cs.MemberCount}");
            }
        }
        var hit = Health(target) < hp0 - 1;
        var fought = closest < ShipAi.StandOff + ShipAi.StandBand * 3 && hit;
        var retaken = pilotKilledAt < 0 || helmRetakenAt >= 0;
        var silenced = gunnersKilledAt < 0 || gunShotsLate == gunShotsAtKill;
        var stopped = allKilledAt < 0 || driftAfterStop < 15;
        Console.WriteLine($"crew: closest {closest:0} m, {w.ShotsFired - shots0} shots, target health {Health(target):0} of {hp0:0} ({hpAtPilotKill:0} when the pilot was shot): {(fought ? "fought" : "DIDN'T FIGHT")}; " +
                          $"helm {(helmRetakenAt >= 0 ? $"taken again {(helmRetakenAt - pilotKilledAt) / (float)World.TickRate:0.0} s after the pilot died" : "NEVER TAKEN AGAIN")}; " +
                          $"gunners' shots {gunShotsAtKill} when shot, {gunShotsLate} a second on, {cs.Ai?.GunnerShots ?? 0} at the end (spares took the seats): {(silenced ? "silenced" : "STILL FIRING")}; " +
                          $"with nobody left it moved {driftAfterStop:0} m in its last 10 s, at {ship.Vel.Length():0.0} m/s: {(stopped ? "stopped" : "DIDN'T STOP")}");
        Console.WriteLine($"crew: the crew's own cost {crew.CpuMs / Math.Max(1, crew.Ticks) * 1000:0.0} us a tick (worst {crew.WorstMs * 1000:0} us at {crew.WorstTick / (float)World.TickRate:0.0} s; worst thinking {crew.WorstThinkMs * 1000:0} us, worst driving {crew.WorstDriveMs * 1000:0} us), {crew.Bytes} bytes allocated in {crew.Ticks} ticks ({crew.AllocTicks} ticks allocated, the last at {crew.LastAllocTick / (float)World.TickRate:0.0} s)");
        var ok = fought && retaken && silenced && stopped;
        Console.WriteLine($"crew: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>The crew alone at scale: <paramref name="members"/> members on copies of <paramref name="craft"/> in a ring, a few near the people, most far.</summary>
    private static bool Scale(int members, string craft)
    {
        var bp = ConstructBuild.FindBlueprint(craft) ?? ConstructBuild.FindBlueprint("Zero Day Frigate");
        if (bp == null)
        {
            Console.WriteLine($"scale: no blueprint \"{craft}\"");
            return false;
        }
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        const int perBody = 24, Near = 20;
        var bodies = (members + perBody - 1) / perBody;
        var mem0 = GC.GetTotalMemory(true);
        var made = 0;
        for (var i = 0; i < bodies; i++)
        {
            // A ring 6 km out, the first Near (20) within two kilometres of the people (awake: driven), the rest asleep.
            var a = i * MathF.Tau / bodies;
            var rad = i < Near ? 400f + i * 80 : 6000f;
            var c = Build(w, bp, new Vector3(MathF.Cos(a) * rad, 0, MathF.Sin(a) * rad), a, place);
            var cs = crew.Crew(c, ShipAiMode.Everyone, spares: Math.Max(0, Math.Min(perBody, members - made) - 2))!;
            made += cs.MemberCount;
        }
        Flush(crew, quiet: true);
        var memCrew = GC.GetTotalMemory(true) - mem0;
        Console.WriteLine($"scale: {crew.Live} crew on {crew.ShipCount} copies of {bp.Name} ({w.Bodies[0].PartCount} parts each); the bodies and crews take {memCrew / 1024} KB");
        var people = new[] { Vector3.Zero };
        crew.Fixed = new ShipAiTarget { At = new Vector3(0, 20, 0), Vel = Vector3.Zero, Body = 0, Player = 255 };
        using (place.In())
        {
            for (var t = 0; t < 4 * World.TickRate; t++) // (warm up: every slice once, the drivers taken)
                crew.Step(w, q, null, people, crewOn: false);
            Flush(crew, quiet: true);
            crew.CpuMs = crew.WorstMs = 0;
            crew.Bytes = 0;
            crew.Ticks = 0;
            var rng = new Random(7);
            for (var t = 0; t < 10 * World.TickRate; t++)
            {
                if (t % 20 == 0 && crew.ShipHigh > 0 && crew.Ships[rng.Next(crew.ShipHigh)] is { MemberCount: > 0 } sh)
                    crew.Kill(sh.Members[rng.Next(sh.MemberCount)], null); // (a death a third of a second: spares walk to fill seats)
                crew.Step(w, q, null, people, crewOn: false);
                crew.Events.Clear();
            }
        }
        var awake = 0;
        for (var i = 0; i < crew.ShipHigh; i++)
            if (crew.Ships[i] is { Tier: not CrewTier.Asleep })
                awake++;
        var per = crew.CpuMs / Math.Max(1, crew.Ticks);
        var ok = per < 1.5 && crew.Bytes == 0;
        Console.WriteLine($"scale: {crew.Live} crew, {awake} bodies awake: {per * 1000:0.0} us a tick on average, worst {crew.WorstMs * 1000:0} us, {crew.Bytes} bytes allocated in {crew.Ticks} ticks " +
                          $"(the records: {crew.Uid.Length} slots, about {crew.Uid.Length * 43 / 1024} KB): {(ok ? "OK" : "OVER BUDGET")}");
        return ok;
    }

    /// <summary>
    /// <c>--crew-check --crew-convoy "A,B,C" [--seconds S]</c> (phase 2): a column of crewed ground vehicles (the first leads), each with a brief made from
    /// its body, driven by their seated crews on the flat floor past a target 150 m off their way. At a third of the run the second vehicle's driver is
    /// shot: it must stop, brakes on, while the rest drive on. Lines every 2 s: each one's speed and how far it is from its place.
    /// </summary>
    private static bool Convoy(string[] names, int seconds)
    {
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var crew = new CrewWorld();
        var bodies = new List<Construct>();
        var back = 0f;
        foreach (var name in names)
        {
            var bp = ConstructBuild.FindBlueprint(name);
            if (bp == null)
            {
                Console.WriteLine($"convoy: no blueprint \"{name}\"");
                return false;
            }
            var c = Build(w, bp, new Vector3(0, 0, back), 0, place); // (heading north, -Z)
            var brief = VehicleBriefMake.Rebuild(c, name);
            brief.Convoy = 1;
            brief.ConvoyIndex = (byte)bodies.Count;
            brief.Leader = bodies.Count > 0 ? bodies[0].Id : (ushort)0;
            if (brief.Attach(c) is { } why)
                Console.WriteLine($"convoy: {name}'s brief doesn't fit ({why})");
            back += c.LocalMax.Z - c.LocalMin.Z + VehicleConvoys.Gap + 4;
            var cs = crew.Crew(c, ShipAiMode.Everyone, faction: _faction);
            Console.WriteLine($"convoy: {name} as {c.Id} ({c.PartCount} parts, {c.Mass / 1000:0.0} t, drive seat {c.DriveSeat}, {c.GunCount} guns): {(cs != null ? crew.Describe(cs) : "NO CREW")}");
            bodies.Add(c);
        }
        Flush(crew);
        if (bodies.Count < 2)
            return false;
        var target = Build(w, ConstructBuild.FindBlueprint(names[^1])!, new Vector3(150, 0, -300), 0, place);
        var people = new[] { Vector3.Zero };
        var ticks = seconds * World.TickRate;
        var killAt = ticks / 3;
        var victim = crew.ShipOf(bodies[1].Id)!;
        var shots0 = w.ShotsFired;
        float worstGap = 0, gapSum = 0;
        var gapN = 0;
        var victimSpeedLate = -1f;
        var leaderMoved = 0f;
        var lead0 = bodies[0].WorldCom;
        for (var t = 0; t < ticks; t++)
        {
            using (place.In())
            {
                crew.Fixed = new ShipAiTarget { At = target.WorldCom, Vel = target.Vel, Body = target.Id, Player = 255 };
                people[0] = bodies[0].WorldCom;
                crew.Step(w, q, null, people, crewOn: false);
                w.Step(q, World.TickDt);
            }
            Flush(crew);
            if (t == killAt && victim.Helm >= 0 && victim.StMember[victim.Helm] is var pm and >= 0)
            {
                // Its driver first: someone else aboard takes the wheel (logged); then everyone aboard, so it stops for good.
                crew.Kill(pm, null);
                Console.WriteLine($"  {t / World.TickRate} s: {names[1]}'s driver shot: {crew.Describe(victim)}");
            }
            if (t == killAt + 6 * World.TickRate)
            {
                Console.WriteLine($"  {t / World.TickRate} s: {names[1]}: {crew.Describe(victim)}; everyone aboard shot");
                for (var i = victim.MemberCount - 1; i >= 0; i--)
                    crew.Kill(victim.Members[i], null);
            }
            if (t > killAt + 16 * World.TickRate)
                victimSpeedLate = MathF.Max(victimSpeedLate, bodies[1].Vel.Length());
            if (t > 10 * World.TickRate)
                for (var k = 2; k < bodies.Count; k++)
                    if (crew.ShipOf(bodies[k].Id)?.Ai is { State: var st } && st.StartsWith("following", StringComparison.Ordinal))
                    {
                        var g = MathF.Abs(float.Parse(st.Split(' ')[1].TrimEnd(','), System.Globalization.CultureInfo.InvariantCulture));
                        if (k != 1)
                        {
                            worstGap = MathF.Max(worstGap, g);
                            gapSum += g;
                            gapN++;
                        }
                    }
            if (t % (2 * World.TickRate) == 2 * World.TickRate - 1)
            {
                var line = string.Join("; ", bodies.Select((b, k) => $"{k}: {b.Vel.Length():0.0} m/s {crew.ShipOf(b.Id)?.Ai?.State ?? "no driver"}"));
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: {line}; shots {w.ShotsFired - shots0}");
            }
        }
        leaderMoved = Vector3.Distance(lead0, bodies[0].WorldCom);
        var moved = leaderMoved > 50;
        var kept = gapN == 0 || gapSum / gapN < 15;
        var stopped = victimSpeedLate < 0.5f;
        Console.WriteLine($"convoy: the leader moved {leaderMoved:0} m: {(moved ? "drove" : "DIDN'T DRIVE")}; followers off their places {(gapN > 0 ? gapSum / gapN : 0):0.0} m on average, worst {worstGap:0}: {(kept ? "kept formation" : "LOST FORMATION")}; " +
                          $"{w.ShotsFired - shots0} shots; the truck whose driver was shot: at most {victimSpeedLate:0.0} m/s 10 s after its crew was shot: {(stopped ? "stopped" : "DIDN'T STOP")}");
        Console.WriteLine($"convoy: the crew's own cost {crew.CpuMs / Math.Max(1, crew.Ticks) * 1000:0.0} us a tick (worst {crew.WorstMs * 1000:0} us at {crew.WorstTick / (float)World.TickRate:0.0} s; thinking {crew.WorstThinkMs * 1000:0}, driving {crew.WorstDriveMs * 1000:0}), {crew.Bytes} bytes allocated in {crew.Ticks} ticks");
        var ok = moved && kept && stopped;
        Console.WriteLine($"convoy: {(ok ? "OK" : "FAILED")}");
        return ok;
    }

    /// <summary>
    /// <c>--crew-check [SHIP] --crew-walk [--speed V] [--turn DEG/S]</c>: the ship's walk graph (CrewNav) built as the game builds it (in slices,
    /// timed), then a standing body walked by CrewNav's follower through the real movement step (<c>World.Simulate</c>, <c>BlockPlayer</c>) from its
    /// first couch to every other station (the helm, each gun seat, the engine room, the magazine), the ship 30 m up over the Proving Ground and
    /// moving at <c>--speed</c> m/s, turning <c>--turn</c> degrees a second. Each walk: there or not, its path, the time. Exit 0 when every walk
    /// to a station on the graph got there.
    /// </summary>
    private static bool Walks(ConstructBuild.Blueprint bp, float speed, float turn)
    {
        if (Maps.Find("Proving Ground") is not { } yard)
        {
            Console.WriteLine("no Proving Ground map");
            return false;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"{bp.Name}: doesn't build: {why}");
            return false;
        }
        c.Rebuild();
        var home = c.Pos + new Vector3(0, 30f, 0);
        WalkPose(w, c, home, c.Rot, Vector3.Zero, Vector3.Zero);
        var nav = new CrewNav();
        CrewNavGraph? g = null;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var ticks = 0;
        while (g == null && ticks < 6000)
        {
            g = nav.Want(c);
            nav.Step(w);
            ticks++;
        }
        var buildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (g != null)
        {
            // Again, timed warm (the first build's slices carry the JIT).
            foreach (var e in nav.Events)
                Console.WriteLine($"  [nav] (cold) {e}");
            nav.Events.Clear();
            nav.Drop(c.Id);
            nav.WorstMs = 0;
            g = null;
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ticks = 0;
            while (g == null && ticks < 6000)
            {
                g = nav.Want(c);
                nav.Step(w);
                ticks++;
            }
            buildMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
        foreach (var e in nav.Events)
            Console.WriteLine($"  [nav] {e}");
        nav.Events.Clear();
        if (g == null)
        {
            Console.WriteLine($"{bp.Name}: no walk graph after {ticks} ticks");
            return false;
        }
        Console.WriteLine($"crew walk: {bp.Name} ({c.PartCount} parts), graph {g.NodeCount} spots in {g.Comps} parts, {g.Nx}x{g.Nz} columns; built in {ticks} ticks, {buildMs:0.0} ms in all, worst tick {nav.WorstMs:0.00} ms; moving {speed:0.#} m/s turning {turn * 180 / MathF.PI:0.#} deg/s");
        // The stations as the crew scans them: seats (feet) and the standing places.
        var spots = new List<(string What, int Node)>();
        var stab = c.StabCount > 0 ? c.Stabs[0].Part : -1;
        var helm = stab >= 0 ? ConstructFlight.SeatFor(c, stab) : c.DriveSeat;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.Seat && !c.Parts[i].Broken)
                spots.Add(($"{(i == helm ? "helm" : c.SeatKindOf(i) == SeatKind.Gunner ? "gun seat" : "seat")} {i} ({c.Parts[i].D.Name})", CrewNav.NearestNode(g, c.ToLocal(ConstructEdits.SeatFeet(c, i)))));
        foreach (var (what, part) in new[] { ("engine room", CrewWorld.WorksStation(c)), ("magazine", CrewWorld.MagazineStation(c)) })
            if (part >= 0)
            {
                c.PartBounds(part, out var mn, out var mx);
                c.SegPose(c.Parts[part].Seg, out var sq, out var sp);
                spots.Add(($"{what} {part} ({c.Parts[part].D.Name})", CrewNav.NearestNode(g, sp + Vector3.Transform(new Vector3((mn.X + mx.X) * 0.5f, mn.Y + 0.1f, (mn.Z + mx.Z) * 0.5f), sq), CrewNav.Reach + 1)));
            }
        var start = spots.FindIndex(s => s.What.StartsWith("seat") && s.Node >= 0);
        if (start < 0)
            start = spots.FindIndex(s => s.Node >= 0);
        if (start < 0)
        {
            Console.WriteLine("  no station has a standing spot on the graph");
            return false;
        }
        Console.WriteLine($"  from {spots[start].What}");
        if (Environment.GetEnvironmentVariable("CREW_NAV_DEBUG") == "2")
        {
            c.EnsureDoors();
            for (var b = 0; b < g.Boxes; b++)
                if (g.BoxAt(b) is var bb && bb.Min.X <= 7.5f && bb.Max.X >= 6.6f && bb.Min.Z <= 16.2f && bb.Max.Z >= 15.3f && bb.Max.Y > 0f && bb.Min.Y < 6f)
                    Console.WriteLine($"  dbg box {b} part {g.BPart[b]} {c.Parts[g.BPart[b]].D.Name} seg {c.Parts[g.BPart[b]].Seg} {bb.Min}..{bb.Max} walk {c.Parts[g.BPart[b]].D.Walk}");
            for (var k = 0; k < c.DoorCount; k++)
            {
                int dp = c.Doors[k].Part;
                Console.WriteLine($"  dbg door {dp} {c.Parts[dp].D.Name} at {c.ToLocal(ConstructRender.PartCentre(c, dp))} upright {CrewNav.Upright(c, dp)} open {ConstructDoors.TargetOpen(c, dp)} locked {ConstructDoors.Locked(c, dp)}");
            }
        }
        if (Environment.GetEnvironmentVariable("CREW_NAV_DEBUG") == "1")
            foreach (var (what, node) in spots)
            {
                if (node < 0)
                    continue;
                var np = g.NodePos(node);
                var size = 0;
                for (var i = 0; i < g.NodeCount; i++)
                    size += g.Comp[i] == g.Comp[node] ? 1 : 0;
                var col = g.ColOf[node];
                var line = $"  dbg {what}: node {node} at {np}, part of {size}; next columns:";
                foreach (var dc in new[] { -g.Nz, g.Nz, -1, 1 })
                {
                    line += " [";
                    for (var k = g.ColStart[col + dc]; k < g.ColStart[col + dc + 1]; k++)
                        line += $"{g.Top[k]:0.00} ";
                    line += "]";
                }
                Span<int> nb = stackalloc int[16];
                var nn = CrewNav.Neighbours(g, node, nb, out _);
                line += $" -> {nn} neighbours:";
                for (var j = 0; j < nn; j++)
                    line += $" {nb[j]}@{g.Top[nb[j]]:0.00} comp {g.Comp[nb[j]]} (mine {g.Comp[node]})";
                Console.WriteLine(line);
            }
        var ok = true;
        var clock = 0;
        var ms = new MoveState();
        for (var k = 0; k < spots.Count; k++)
        {
            if (k == start)
                continue;
            var (what, node) = spots[k];
            if (node < 0)
            {
                Console.WriteLine($"  {what}: no standing spot near it (a timed walk in the game)");
                continue;
            }
            if (g.Comp[node] != g.Comp[spots[start].Node])
            {
                Console.WriteLine($"  {what}: cut off (another part of the graph): not walked");
                continue;
            }
            var walker = nav.Walk(g, 1, spots[start].Node, node);
            var at = c.ToWorld(g.NodePos(spots[start].Node) + new Vector3(0, 0.03f, 0));
            ms = new MoveState { Position = at, Velocity = c.PointVel(at) };
            ConstructRide.End(ref ms, null, c, 0, Vector3.Zero, frame: true); // (crew bodies ride the ship's own frame, as people do)
            byte phase = 0;
            var t = 0;
            var travelled = 0f;
            var last = c.ToLocal(ms.Position);
            for (; t < 60 * 60 && phase < 2; t++)
            {
                var kk = MathF.Min(1f, clock++ / 120f);
                var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                var vel = fwd * speed * kk;
                var ang = new Vector3(0, turn * kk, 0);
                WalkPose(w, c, c.Pos + vel * World.TickDt, Quaternion.CreateFromAxisAngle(Vector3.UnitY, ang.Y * World.TickDt) * c.Rot, vel, ang);
                nav.Step(w);
                ConstructDoors.Step(w);
                phase = nav.Steer(walker, c, w, ref ms, out var cmd);
                if (phase >= 2)
                    break;
                cmd.Seq = (uint)t;
                World.Simulate(ref ms, cmd);
                w.BlockPlayer(ref ms, World.HeightOf(ms.Stance), server: true, who: 1);
                var now = c.ToLocal(ms.Position);
                if (Environment.GetEnvironmentVariable("CREW_NAV_DEBUG") == "2" && (t % 30 == 0 || !ms.Grounded || t > 150 && t < 167))
                    Console.WriteLine($"    t{t}: at {now}, leg {nav.WAt[walker]}/{nav.WCount[walker]} to {(nav.WAt[walker] < nav.WCount[walker] ? nav.WPts[walker * CrewNav.MaxPts + nav.WAt[walker]] : default)}, grounded {ms.Grounded}, climb {ms.RideClimb} mantle {ms.MantleTicks} ride {ms.RideBody}, ladder {nav.WLadder[walker * CrewNav.MaxPts + Math.Min(nav.WAt[walker], CrewNav.MaxPts - 1)]}, vel {ms.Velocity}");
                travelled += Vector3.Distance(now, last);
                last = now;
            }
            var end = c.ToLocal(ms.Position);
            var there = phase == 2 && Vector3.Distance(end, g.NodePos(node)) < 1.2f;
            ok &= there;
            Console.WriteLine($"  {what}: {(there ? "there" : phase == 3 ? "GAVE UP" : "NOT THERE")} in {t / (float)World.TickRate:0.0} s, path {nav.WLength[walker]:0.0} m in {nav.WCount[walker]} legs, walked {travelled:0.0} m, ended {Vector3.Distance(end, g.NodePos(node)):0.00} m off, riding {(ms.RideBody == c.Id ? "it" : ms.RideBody == 0 ? "nothing" : $"body {ms.RideBody}")}");
            nav.End(walker);
            if (k == (start == 0 ? 1 : 0))
            {
                Console.WriteLine($"  (first walk's worst nav tick {nav.WorstMs:0.00} ms: JIT and the search arrays' first growth; timed afresh from here)");
                nav.WorstMs = 0;
            }
            foreach (var e in nav.Events)
                Console.WriteLine($"  [nav] {e}");
            nav.Events.Clear();
        }
        Console.WriteLine($"  nav cost: {nav.CpuMs / Math.Max(1, nav.Tick):0.000} ms a tick on average, worst {nav.WorstMs:0.00} ms");
        return ok;
    }

    /// <summary>
    /// <c>--crew-check BLUEPRINT --crew-work [--crew-faction NAME]</c> (phase 7): the blueprint built on the ground and crewed as a nobody's of that kind
    /// (Traders by default); its work stations (the capital gun's spotter, lifters, cargo docks, Building Stations) and who works them; the gate
    /// (<see cref="CrewWorld.Works"/>) open while each is manned, shut once its hand is killed, open again when a spare walks over. Exit 0 when so.
    /// </summary>
    private static bool Work(ConstructBuild.Blueprint bp, byte faction)
    {
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 0, 0), 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"{bp.Name}: doesn't build: {why}");
            return false;
        }
        c.Rebuild();
        c.Owner = 255;
        var crew = new CrewWorld();
        var s = crew.Crew(c, ShipAiMode.Strangers, faction: faction, spares: 2)!;
        Flush(crew);
        var people = new[] { c.WorldCom + new Vector3(0, 0, 50) };
        for (var t = 0; t < 30; t++)
            crew.Step(w, World.Query, null, people, crewOn: false);
        Flush(crew);
        var works = 0;
        var ok = true;
        for (var k = 0; k < s.StCount; k++)
            if (s.StKind[k] is CrewStationKind.Spot or CrewStationKind.Crane or CrewStationKind.Dock or CrewStationKind.Build)
                works++;
        Console.WriteLine($"  guns' reach: {string.Join(", ", Enumerable.Range(0, c.GunCount).Select(k => $"{c.Parts[c.Guns[k].Part].D.Name} {Size(c, c.Guns[k].Part):0.00} m3"))}");
        Console.WriteLine($"crew work: {bp.Name} ({c.PartCount} parts) as {CrewFactions.Of(faction)?.Name}: {works} work stations; {crew.Describe(s)}");
        for (var k = 0; k < s.StCount; k++)
        {
            var kind = s.StKind[k];
            if (kind is not (CrewStationKind.Spot or CrewStationKind.Crane or CrewStationKind.Dock or CrewStationKind.Build))
                continue;
            int part = s.StPart[k];
            var m = s.StMember[k];
            var open = crew.Works(c, part);
            var line = $"  {CrewWorld.StationName(kind)} at {c.Parts[part].D.Name} ({part}): {(m >= 0 ? $"{crew.Job[m]} {crew.Uid[m]}" : "nobody")}, works {open}";
            if (m >= 0)
            {
                crew.Kill(m, null);
                var shut = !crew.Works(c, part) || kind == CrewStationKind.Spot;
                var spotGone = kind != CrewStationKind.Spot || crew.Spotted(s) < 0;
                var back = false;
                for (var t = 0; t < 60 * 30 && !back; t++)
                {
                    crew.Step(w, World.Query, null, people, crewOn: false);
                    back = s.StMember[k] >= 0 && crew.State[s.StMember[k]] == CrewState.Station;
                }
                line += $"; its hand killed: works {crew.Works(c, part)}{(kind == CrewStationKind.Spot ? $" (spotted {(spotGone ? "no" : "yes")})" : "")}, {(back ? $"a spare took it: works {crew.Works(c, part)}" : "nobody left to take it")}";
                ok &= open && shut && spotGone && (!back || crew.Works(c, part));
            }
            else
                ok &= kind == CrewStationKind.Spot || !open;
            Console.WriteLine(line);
            Flush(crew, quiet: true);
        }
        return ok && works > 0;
    }

    private static float Size(Construct c, int part)
    {
        c.PartBounds(part, out var mn, out var mx);
        return (mx.X - mn.X) * (mx.Y - mn.Y) * (mx.Z - mn.Z);
    }

    private static void WalkPose(ConstructWorld w, Construct c, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 ang)
    {
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        c.Pos = pos;
        c.Rot = Quaternion.Normalize(rot);
        c.Vel = vel;
        c.AngVel = ang;
        c.Asleep = false;
        c.Anchored = false;
        ConstructRide.BeforePhysics(w);
    }

    private static void Flush(CrewWorld crew, bool quiet = false)
    {
        if (!quiet)
            foreach (var e in crew.Events)
                Console.WriteLine($"  [crew] {e}");
        crew.Events.Clear();
    }

    private static float Health(Construct c)
    {
        var h = 0f;
        for (var i = 0; i < c.PartCount; i++)
            if (!c.Parts[i].Broken)
                h += c.Parts[i].Health;
        return h;
    }
}
