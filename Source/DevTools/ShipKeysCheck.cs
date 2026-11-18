using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--ship-keys-check [BLUEPRINT,...] [--gravity G]</c>: each shipped ship flown on Ship keys (or the named blueprints) is held
/// still in a flat world (zero gravity by default), and each key is held in turn: W, S, A, D, Shift, Ctrl, Q, E. For each it
/// prints where the ship went and how it turned (against holding no key), the thrusters' mean push and which fired, and FAIL when
/// it didn't go the way the key asks (Karl, 2026-11-18: the Zero Day's W lit the wrong jets). <c>--jets</c> lists every thruster.
/// A dev tool: it allocates freely.
/// </summary>
public static class ShipKeysCheck
{
    private static readonly (string Name, ushort Keys, int Axis, float Sign)[] Keys =
    [
        ("W", (ushort)Buttons.Forward, 0, 1), ("S", (ushort)Buttons.Back, 0, -1), ("D", (ushort)Buttons.Right, 2, 1), ("A", (ushort)Buttons.Left, 2, -1),
        ("Shift", (ushort)Buttons.Sprint, 1, 1), ("Ctrl", (ushort)Buttons.Crouch, 1, -1), ("E", ConstructParts.KeyRollRight, 4, 1), ("Q", ConstructParts.KeyRollLeft, 4, -1),
    ];

    public static int Cli(string[] args)
    {
        var named = args.SkipWhile(a => !a.Equals("--ship-keys-check", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var names = named == null || named.StartsWith("--") ? null : named.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var gArg = args.SkipWhile(a => !a.Equals("--gravity", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var gravity = gArg != null && float.TryParse(gArg, System.Globalization.CultureInfo.InvariantCulture, out var gv) ? gv : 0f;
        var place = VehicleDriveCheck.Place(gravity);
        var bad = 0;
        foreach (var bp in ConstructBuild.Blueprints)
        {
            if (names != null && !names.Any(n => n.Equals(bp.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var w = new ConstructWorld();
            var job = new ConstructBuild.GroundJob(w, bp, null);
            while (!job.Step(0.4))
            {
            }
            Construct? c;
            using (place.In())
                c = job.Join(new Vector3(0, 200, 0), 0, out _);
            if (c == null || c.StabCount == 0 || c.ThrusterCount == 0)
                continue;
            ref var sp = ref c.Parts[c.Stabs[0].Part];
            if (ConstructFlight.KeysOf(c, sp, sp.D) != FlightPattern.Ship)
                continue;
            var seat = ConstructFlight.SeatFor(c, c.Stabs[0].Part);
            if (seat < 0)
            {
                Console.WriteLine($"{bp.Name}: no seat flies its stabilizer");
                continue;
            }
            var anyFed = false;
            for (var k = 0; k < c.ThrusterCount; k++)
                anyFed |= c.Thrusters[k].Fed;
            if (!anyFed)
            {
                Console.WriteLine($"{bp.Name}: no thruster has fuel, so no key can push it (the Limpet: steered straight by its Board order, docs/sandbox/constructs/limpet.md)");
                continue;
            }
            c.Driver = 1;
            c.DriverSeat = seat;
            Console.WriteLine($"{bp.Name}: {c.ThrusterCount} thrusters, {c.Mass / 1000:0.0} t, stabilizer turned {Show(Vector3.Transform(-Vector3.UnitZ, Rot24.Quat(sp.Rot)))} forward, {Show(Vector3.Transform(Vector3.UnitY, Rot24.Quat(sp.Rot)))} up");
            var mouse = ConstructConfig.Get(sp, sp.D, CfgKey.MouseAim, 0) >= 0.5f;
            var nose = Vector3.Transform(Vector3.Transform(-Vector3.UnitZ, Rot24.Quat(sp.Rot)), c.Rot);
            c.Parts[seat].AimYaw = MathF.Atan2(nose.X, -nose.Z); // (the seat looks where the nose points: mouse aim holds it there)
            c.Parts[seat].AimPitch = MathF.Asin(Math.Clamp(nose.Y, -1, 1));
            if (args.Any(a => a.Equals("--jets", StringComparison.OrdinalIgnoreCase)))
                for (var k = 0; k < c.ThrusterCount; k++)
                {
                    ref var th = ref c.Thrusters[k];
                    ref var tp = ref c.Parts[th.Part];
                    Console.WriteLine($"  com {Show(c.Com)} jet {k}: {tp.D.Name} at {Show(th.At)} pushes {Show(th.Dir)}, fed {th.Fed}, working {tp.Working}, scanned {th.Scanned}, lifts {th.Lifts}");
                }
            var home = c.Pos;
            var homeCom = c.WorldCom;
            var rot = c.Rot;
            // Where it goes with no key held (it may sink under gravity until its jets spool): each key is judged against it.
            var (still, stillTurn, _, _, _) = Hold(w, place, c, 0, home, homeCom, rot);
            foreach (var (name, keys, axisKey, sign) in Keys)
            {
                var axis = axisKey == 2 && !mouse ? 5 : axisKey; // (keys alone: A and D turn it)
                var (moved, turned, f, t, firing) = Hold(w, place, c, keys, home, homeCom, rot);
                moved -= still;
                turned -= stillTurn;
                // moved: forward, up, right (m); turned: nose up, right side down, nose right (deg); the asked way the most of them.
                float[] went = [moved.X, moved.Y, moved.Z, turned.X, turned.Y, turned.Z];
                var want = went[axis] * sign;
                var ok = axis < 3 ? want > 0.1f && want >= 0.8f * MathF.Max(MathF.Abs(moved.X), MathF.Max(MathF.Abs(moved.Y), MathF.Abs(moved.Z)))
                    : want > 1f && want >= 0.8f * MathF.Max(MathF.Abs(turned.X), MathF.Max(MathF.Abs(turned.Y), MathF.Abs(turned.Z)));
                if (!ok)
                    bad++;
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-5} went fwd {moved.X,5:0.00} up {moved.Y,5:0.00} right {moved.Z,5:0.00} m, nose up {turned.X,5:0.0} right side down {turned.Y,5:0.0} nose right {turned.Z,5:0.0} deg   push fwd {f.X / 1000,6:0} up {f.Y / 1000,6:0} right {f.Z / 1000,6:0} kN   {firing}");
            }
            if (mouse && args.Any(a => a.Equals("--dive", StringComparison.OrdinalIgnoreCase)))
            {
                // Looked 70 degrees down for six seconds (the flight check's turn): its pitch every half second and the jets lit.
                var (aimYaw, aimPitch) = (c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
                Hold(w, place, c, 0, home, homeCom, rot, () => c.Parts[seat].AimPitch = aimPitch - 70 * MathF.PI / 180);
                c.Pos = c.PrevPos = home;
                c.Rot = c.PrevRot = rot;
                c.Vel = c.AngVel = Vector3.Zero;
                var diveSpeed = args.SkipWhile(a => !a.Equals("--dive", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } ds && float.TryParse(ds, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 0f;
                c.Vel = Vector3.Transform(Vector3.Transform(-Vector3.UnitZ, Rot24.Quat(sp.Rot)), c.Rot) * diveSpeed; // (--dive SPEED: going forward at it, as after the flight check's W)
                for (var half = 0; half < 12; half++)
                {
                    var (_, _, firing) = Fly(w, place, c, 0, 30);
                    var diveNose = Vector3.Transform(Vector3.Transform(-Vector3.UnitZ, Rot24.Quat(sp.Rot)), c.Rot);
                    Console.WriteLine($"  dive {(half + 1) * 0.5f:0.0} s: nose {MathF.Asin(Math.Clamp(diveNose.Y, -1, 1)) * 180 / MathF.PI:0.0} deg, pitch rate {Vector3.Dot(c.AngVel, Vector3.Transform(Vector3.UnitX, c.Rot)) * 180 / MathF.PI:0.0} deg/s, climb {c.Vel.Y:0.0} m/s; {firing}");
                }
                c.Parts[seat].AimPitch = aimPitch;
                c.Parts[seat].AimYaw = aimYaw;
            }
            if (mouse)
                foreach (var (name, dPitch, dYaw, axis, sign) in new[] { ("look up", 0.26f, 0f, 0, 1f), ("look down", -0.26f, 0f, 0, -1f), ("look right", 0f, 0.26f, 2, 1f), ("look left", 0f, -0.26f, 2, -1f) })
                {
                    // The mouse: the seat looks 15 degrees off the nose, and the nose should follow.
                    var (aimYaw, aimPitch) = (c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
                    var (_, turned, f, _, firing) = Hold(w, place, c, 0, home, homeCom, rot, () => { c.Parts[seat].AimYaw = aimYaw + dYaw; c.Parts[seat].AimPitch = aimPitch + dPitch; });
                    c.Parts[seat].AimYaw = aimYaw;
                    c.Parts[seat].AimPitch = aimPitch;
                    turned -= stillTurn;
                    float[] got = [turned.X, turned.Y, turned.Z];
                    var ok = got[axis] * sign > 1f && got[axis] * sign >= 0.8f * MathF.Max(MathF.Abs(turned.X), MathF.Max(MathF.Abs(turned.Y), MathF.Abs(turned.Z)));
                    if (!ok)
                        bad++;
                    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-10} nose up {turned.X,5:0.0} right side down {turned.Y,5:0.0} nose right {turned.Z,5:0.0} deg   {firing}");
                }
        }
        Console.WriteLine(bad == 0 ? "every key pushes its way" : $"{bad} key(s) push the wrong way");
        return bad;
    }

    /// <summary>
    /// Back where it started, still, its stabilizer armed and holding a second; then the keys held three quarters of a second. Where its centre of mass went (forward, up, right, m) and how it
    /// turned (nose up, right side down, nose right, degrees), in its own axes at the start; the thrusters' mean push (body axes) and which fired.
    /// </summary>
    private static (Vector3 Moved, Vector3 Turned, Vector3 F, Vector3 T, string Firing) Hold(ConstructWorld w, VehicleDriveCheck.CheckPlace place, Construct c, ushort keys, Vector3 home, Vector3 homeCom, Quaternion rot, Action? then = null)
    {
        c.Pos = c.PrevPos = home;
        c.Rot = c.PrevRot = rot;
        c.Vel = c.AngVel = Vector3.Zero;
        c.Parts[c.Stabs[0].Part].State |= 1; // (armed, as after take-off: let go, it holds where it is, its jets spooled for that)
        Fly(w, place, c, 0, 60);
        c.Pos = c.PrevPos = home;
        c.Rot = c.PrevRot = rot;
        c.Vel = c.AngVel = Vector3.Zero;
        then?.Invoke();
        var (f, t, firing) = Fly(w, place, c, keys, 45);
        var inv = Quaternion.Conjugate(rot);
        var moved = Vector3.Transform(c.WorldCom - homeCom, inv); // (its centre of mass: a turn swings the origin round it)
        var turn = Quaternion.Multiply(inv, c.Rot);
        var nose = Vector3.Transform(-Vector3.UnitZ, turn);
        var right = Vector3.Transform(Vector3.UnitX, turn);
        static float Deg(float x) => MathF.Asin(Math.Clamp(x, -1, 1)) * 180 / MathF.PI;
        return (new Vector3(-moved.Z, moved.Y, moved.X), new Vector3(Deg(nose.Y), Deg(-right.Y), Deg(nose.X)), f, t, firing);
    }

    private static string Show(Vector3 v) => $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})";

    /// <summary>Holds the keys for some ticks; the thrusters' push and twist at the end, in the body's frame, and which fire.</summary>
    private static (Vector3 F, Vector3 T, string Firing) Fly(ConstructWorld w, VehicleDriveCheck.CheckPlace place, Construct c, ushort keys, int ticks)
    {
        var q = new SelfTest.DriveWorld();
        Vector3 f = Vector3.Zero, tq = Vector3.Zero;
        var firing = new Dictionary<string, int>();
        for (var t = 0; t < ticks; t++)
        {
            using (place.In())
            {
                c.Parts[c.DriverSeat].Keys = (ushort)(keys | CPart.Occupied);
                ConstructEdits.DriveInput(c, (Buttons)(keys & 0xff), World.TickDt);
                w.Step(q, World.TickDt);
            }
            // The push and twist summed over the hold (a ship on dampeners stops pushing once it goes as fast as it was asked).
            for (var k = 0; k < c.ThrusterCount; k++)
            {
                ref var th = ref c.Thrusters[k];
                if (th.Level < 0.05f)
                    continue;
                ref var p = ref c.Parts[th.Part];
                var push = th.Dir * (p.D.ThrustForce * th.Level / ticks); // (the segment's frame: these ships' thrusters hang on the body)
                f += push;
                tq += Vector3.Cross(th.At - c.Com, push);
                var dir = MathF.Abs(th.Dir.Y) > 0.7f ? th.Dir.Y > 0 ? "up" : "down" : MathF.Abs(th.Dir.X) > 0.7f ? th.Dir.X > 0 ? "right" : "left" : th.Dir.Z < 0 ? "fwd" : "back";
                var key = $"{p.D.Name} {dir}";
                firing[key] = Math.Max(firing.GetValueOrDefault(key), 1);
            }
        }
        // Body axes: forward -z, up +y, right +x; pitch about +x (nose up), roll about -z (right side down), yaw about -y (nose right).
        return (new Vector3(-f.Z, f.Y, f.X), new Vector3(tq.X, -tq.Z, -tq.Y), string.Join(", ", firing.Keys));
    }
}
