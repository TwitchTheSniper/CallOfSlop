using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check [SHIP] [--target NAME] [--range M] [--seconds S] [--still] [--scale N]</c> (was <c>--ship-ai-check</c>; docs/sandbox/design/bot-brain.md):
/// a crewed ship (default "Zero Day Frigate") flown by its seated crew (CrewWorld, Karl's ShipAi drivers behind the rule-1 gate) against a target ship
/// <c>--range</c> m north (1200; flown by script unless <c>--still</c>), on the flight check's flat floor, in a world of its own with no game (so no
/// bodies: every member is a record at its seat). 120 s by default, in acts: it fights; at 40 s its pilot is killed (the helm keeps its last input for
/// <see cref="CrewWorld.HelmGraceTicks"/>, then a spare walks over and takes it); at 70 s its gunners are killed (their guns fall silent); at 90 s
/// everyone left at the helm or spare is killed (the ship stops: it holds). A line every two seconds and a summary; exit 0 when each act did what it
/// should. <c>--scale N</c> then times the crew alone with N members on many copies of a small crewed craft, most far from the people (asleep),
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
        var scale = int.TryParse(Arg("--scale"), out var sc) ? sc : 0;
        var shipBp = ConstructBuild.FindBlueprint(shipName);
        var targetBp = ConstructBuild.FindBlueprint(targetName);
        if (shipBp == null || targetBp == null)
        {
            Console.WriteLine($"no blueprint named \"{(shipBp == null ? shipName : targetName)}\" among {ConstructBuild.Blueprints.Count} ({ConstructBuild.RefusedBlueprints.Count} refused: {string.Join("; ", ConstructBuild.RefusedBlueprints.Take(3))})");
            return 1;
        }
        var ok = seconds <= 0 || Fight(shipBp, targetBp, shipName, targetName, range, seconds, still);
        if (scale > 0)
            ok &= Scale(scale, Arg("--scale-ship") ?? "Bandit Helicopter 1");
        return ok ? 0 : 1;
    }

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
        var cs = crew.Crew(ship, ShipAiMode.Everyone)!;
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
            if (t == 72 * World.TickRate)
                gunShotsLate = ai?.GunnerShots ?? 0;
            if (t == 90 * World.TickRate)
            {
                allKilledAt = t;
                for (var i = cs.MemberCount - 1; i >= 0; i--)
                    crew.Kill(cs.Members[i], null);
                Console.WriteLine($"  90 s: everyone left is shot: {crew.Describe(cs)}");
            }
            if (allKilledAt >= 0 && t == allKilledAt + CrewWorld.HelmGraceTicks + World.TickRate)
                stopAt = ship.WorldCom;
            if (allKilledAt >= 0 && t > allKilledAt + CrewWorld.HelmGraceTicks + World.TickRate)
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
        var silenced = gunnersKilledAt < 0 || (cs.Ai?.GunnerShots ?? 0) == gunShotsLate;
        var stopped = allKilledAt < 0 || driftAfterStop < 40;
        Console.WriteLine($"crew: closest {closest:0} m, {w.ShotsFired - shots0} shots, target health {Health(target):0} of {hp0:0} ({hpAtPilotKill:0} when the pilot was shot): {(fought ? "fought" : "DIDN'T FIGHT")}; " +
                          $"helm {(helmRetakenAt >= 0 ? $"taken again {(helmRetakenAt - pilotKilledAt) / (float)World.TickRate:0.0} s after the pilot died" : "NEVER TAKEN AGAIN")}; " +
                          $"gunners' shots {gunShotsAtKill} when shot, {gunShotsLate} two seconds on, {cs.Ai?.GunnerShots ?? 0} at the end: {(silenced ? "silenced" : "STILL FIRING")}; " +
                          $"with nobody left it moved {driftAfterStop:0} m: {(stopped ? "stopped" : "DIDN'T STOP")}");
        Console.WriteLine($"crew: the crew's own cost {crew.CpuMs / Math.Max(1, crew.Ticks) * 1000:0.0} us a tick (worst {crew.WorstMs * 1000:0} us), {crew.Bytes} bytes allocated in {crew.Ticks} ticks");
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
        const int perBody = 24;
        var bodies = (members + perBody - 1) / perBody;
        var mem0 = GC.GetTotalMemory(true);
        var made = 0;
        for (var i = 0; i < bodies; i++)
        {
            // A ring 5 km out, the first eight within a kilometre of the people (awake: driven), the rest asleep.
            var a = i * MathF.Tau / bodies;
            var rad = i < 8 ? 400f + i * 60 : 6000f;
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
