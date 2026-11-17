using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// <c>--ship-ai-check [SHIP] [--target NAME] [--range M] [--seconds S] [--still]</c>: an AI crew (ConstructShipAi.cs) flown against a target in a world of
/// its own, on the flight check's flat floor: the crewed ship (default "Zero Day Frigate") stands at the origin, the target ship (default the same blueprint)
/// <c>--range</c> metres north (1200), flown by script from its own pilot's seat (it lifts off, then slides from side to side) unless <c>--still</c>. 90 s by default. Prints a line every two
/// seconds (range, height, speed, what the crew is doing, shots, launches, the target's health) and a summary; exit 0 when the crew closed to its guns'
/// range and hit the target. Allocates (a tool, never on a tick).
/// </summary>
public static class ShipAiCheck
{
    public static int Cli(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var at = Array.FindIndex(args, a => a.Equals("--ship-ai-check", StringComparison.OrdinalIgnoreCase));
        var shipName = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "Zero Day Frigate";
        var targetName = Arg("--target") ?? "Zero Day Frigate";
        var range = float.TryParse(Arg("--range"), System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 1200;
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 90;
        var still = args.Any(a => a.Equals("--still", StringComparison.OrdinalIgnoreCase));
        var shipBp = ConstructBuild.FindBlueprint(shipName);
        var targetBp = ConstructBuild.FindBlueprint(targetName);
        if (shipBp == null || targetBp == null)
        {
            Console.WriteLine($"no blueprint named \"{(shipBp == null ? shipName : targetName)}\" among {ConstructBuild.Blueprints.Count} ({ConstructBuild.RefusedBlueprints.Count} refused: {string.Join("; ", ConstructBuild.RefusedBlueprints.Take(3))})");
            return 1;
        }
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        Construct? Build(ConstructBuild.Blueprint bp, Vector3 ground, float yaw)
        {
            var job = new ConstructBuild.GroundJob(w, bp, null);
            while (!job.Step(1000))
            {
            }
            using (place.In())
                return job.Join(ground, yaw, out var why) ?? throw new InvalidOperationException($"{bp.Name} doesn't build: {why}");
        }
        var ship = Build(shipBp, Vector3.Zero, MathF.PI); // (facing south, away from the target: it must turn to fight)
        var target = Build(targetBp, new Vector3(0, 0, -range), 0)!;
        var ai = new ShipAi(ship!.Id, ShipAiMode.Everyone);
        var tSeat = target.StabCount > 0 ? ConstructFlight.SeatFor(target, target.Stabs[0].Part) : -1;
        var hp0 = Health(target);
        var shots0 = w.ShotsFired;
        var shells0 = w.ShellsFired;
        var closest = float.MaxValue;
        Console.WriteLine($"ship-ai: {shipName} ({ship.PartCount} parts, {ship.Mass / 1000:0} t) against {targetName} at {range:0} m{(still ? ", standing" : ", flying")}");
        using (place.In())
            Console.WriteLine($"  crew: {ai.Describe(ship)}");
        var ticks = seconds * World.TickRate;
        for (var t = 0; t < ticks; t++)
        {
            using (place.In())
            {
                if (tSeat >= 0 && !still && target.PartCount > tSeat)
                {
                    // The target's pilot: up for five seconds, then sliding one way and then the other every eight, forward a little in turns.
                    var keys = (Buttons)0;
                    var sec = t / World.TickRate;
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
                var tg = new ShipAiTarget { At = target.WorldCom, Vel = target.Vel, Body = target.Id, Player = 255 };
                ai.Step(w, ship, q, target.PartCount > 0, tg);
                w.Step(q, World.TickDt);
            }
            var d = Vector3.Distance(ship.WorldCom, target.WorldCom);
            closest = MathF.Min(closest, d);
            if (t % (2 * World.TickRate) == 2 * World.TickRate - 1)
            {
                var fwd = Vector3.Transform(-Vector3.UnitZ, ship.Rot);
                var to = Vector3.Normalize(target.WorldCom - ship.WorldCom);
                var face = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(fwd with { Y = 0 }), Vector3.Normalize(to with { Y = 0 })), -1, 1)) * 180 / MathF.PI;
                Console.WriteLine($"  {(t + 1) / World.TickRate,3} s: range {d,5:0} m, height {ship.WorldCom.Y,4:0} m (target {target.WorldCom.Y:0}), speed {ship.Vel.Length(),3:0} m/s, " +
                                  $"{face,3:0} deg off its nose, {ai.State}; shots {w.ShotsFired - shots0}, launches {ai.Launches} ({w.ShellsFired - shells0} shells), flares {ai.Flares}, target health {Health(target) / hp0 * 100:0}%");
            }
        }
        var hit = Health(target) < hp0 - 1;
        var ok = closest < ShipAi.StandOff + ShipAi.StandBand * 3 && hit;
        Console.WriteLine($"ship-ai: closest {closest:0} m, {w.ShotsFired - shots0} shots, {ai.Launches} launches, target health {Health(target):0} of {hp0:0}: {(ok ? "OK" : "FAILED")}");
        return ok ? 0 : 1;
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
