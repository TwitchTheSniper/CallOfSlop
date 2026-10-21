using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Grenade range" (see SelfTest.cs): how far each throwable carries on flat ground, that a frag
/// thrown as far as it goes lands before its fuse runs out, that smoke pops on the ground, that bots' throws go off
/// at their target, and that flight is deterministic.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The standard throw speed before the range was extended (m/s), to report the old range.</summary>
    private const float OldThrowSpeed = 17f;

    private static void CheckGrenadeRange()
    {
        var flat = MapFile.Load("""
            { "name": "Grenade range test", "halfX": 60, "halfZ": 90, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Spawn", "x": 0, "z": 80 } ] }
            """, "grenade-range.json");
        World.SetMap(flat);
        try
        {
            GrenadeRanges();
            GrenadeBotAim();
            GrenadeDeterminism();
        }
        finally
        {
            World.SetMap(Maps.All[0]);
        }
    }

    /// <summary>Thrower's eye on the test map (standing at z = 75, throwing towards -Z).</summary>
    private static readonly Vector3 RangeEye = new(0, World.EyeHeight, 75);

    /// <summary>
    /// A throw at <paramref name="pitch"/> (radians) towards -Z flown with the real <see cref="Throwables.Step(Projectile, float)"/>
    /// until it goes off: first touch (horizontal distance and time), where it went off, and when.
    /// </summary>
    private static (float firstDist, float firstTime, Vector3 end, float endTime) FlyThrow(ThrowableDef def, Vector3 eye, float yaw, float pitch)
    {
        var dir = World.Forward(yaw, pitch);
        var p = new Projectile { Kind = def.Kind, Pos = Throwables.LaunchPoint(eye, dir), Vel = Throwables.LaunchVelocity(def, dir, Vector3.Zero) };
        float firstDist = -1, firstTime = -1;
        var landed = false;
        for (var tick = 1; tick <= 10 * World.TickRate; tick++)
        {
            var hit = Throwables.Step(p, World.TickDt);
            var t = tick * World.TickDt;
            if (hit && !landed)
            {
                firstDist = new Vector2(p.Pos.X - eye.X, p.Pos.Z - eye.Z).Length();
                firstTime = t;
            }
            landed |= hit;
            if (Throwables.GoesOff(def, t, hit, landed))
                return (firstDist, firstTime, p.Pos, t);
        }
        return (firstDist, firstTime, p.Pos, 10);
    }

    /// <summary>Longest first-touch distance over pitches 0..70 degrees, and the pitch that gives it.</summary>
    private static (float dist, float time, int deg) MaxCarry(ThrowableDef def)
    {
        (float dist, float time, int deg) best = (0, 0, 0);
        for (var deg = 0; deg <= 70; deg++)
        {
            // Measure carry with a long fuse so even an impact or smoke flies until it lands.
            var probe = new ThrowableDef { Kind = ThrowableKind.Frag, Name = def.Name, Blurb = "", Fuse = 9f, ThrowSpeed = def.ThrowSpeed };
            var (d, t, _, _) = FlyThrow(probe, RangeEye, 0, deg * MathF.PI / 180f);
            if (d > best.dist)
                best = (d, t, deg);
        }
        return best;
    }

    private static void GrenadeRanges()
    {
        foreach (var kind in new[] { ThrowableKind.Frag, ThrowableKind.Impact, ThrowableKind.Smoke, ThrowableKind.Recon })
        {
            var def = Throwables.Get(kind);
            var old = new ThrowableDef { Kind = kind, Name = def.Name, Blurb = "", ThrowSpeed = OldThrowSpeed };
            var (now, nowTime, deg) = MaxCarry(def);
            var (before, _, _) = MaxCarry(old);
            var at45 = FlyThrow(new ThrowableDef { Kind = ThrowableKind.Frag, Name = "", Blurb = "", Fuse = 9f, ThrowSpeed = def.ThrowSpeed }, RangeEye, 0, MathF.PI / 4);
            Line($"{def.Name}: {def.ThrowSpeed:0.0} m/s carries {now:0.0} m on flat ground (best at {deg} deg, lands after {nowTime:0.00} s; "
                 + $"45 deg {at45.firstDist:0.0} m), was {before:0.0} m at {OldThrowSpeed:0} m/s ({now / before:0.00}x)");
            if (now < 30f || now > 40f)
                Fail($"{def.Name}: max throw range {now:0.0} m, expected 30-40 m");
            if (now < before * 1.5f)
                Fail($"{def.Name}: range only {now / before:0.00}x the old {before:0.0} m");
        }

        // A frag thrown as far as it goes (and at 45 degrees) lands before it explodes, with time to spare.
        var frag = Throwables.Get(ThrowableKind.Frag);
        var (fragMax, _, fragDeg) = MaxCarry(frag);
        foreach (var deg in new[] { fragDeg, 45 })
        {
            var (d, t, end, endT) = FlyThrow(frag, RangeEye, 0, deg * MathF.PI / 180f);
            var rolled = new Vector2(end.X - RangeEye.X, end.Z - RangeEye.Z).Length();
            Line($"frag at {deg} deg: lands {d:0.0} m out at {t:0.00} s, explodes at {endT:0.00} s (fuse {frag.Fuse:0.0} s) {rolled:0.0} m out, {end.Y:0.00} m up");
            if (t < 0 || t > frag.Fuse - 0.2f)
                Fail($"frag at {deg} deg: lands at {t:0.00} s, less than 0.2 s before its {frag.Fuse:0.0} s fuse (air burst)");
            if (end.Y > 2.5f)
                Fail($"frag at {deg} deg exploded {end.Y:0.00} m up (higher than a bounce goes)");
        }
        // Steepest throw that still lands before the fuse.
        var steepest = 0;
        for (var deg = 0; deg <= 89; deg++)
            if (FlyThrow(frag, RangeEye, 0, deg * MathF.PI / 180f).firstTime is var ft and > 0 && ft <= frag.Fuse)
                steepest = deg;
        Line($"frag: lands before its fuse when thrown up to {steepest} deg (max range {fragMax:0.0} m)");

        // Smoke thrown far pops on the ground, not in the air.
        var smoke = Throwables.Get(ThrowableKind.Smoke);
        var s = FlyThrow(smoke, RangeEye, 0, 40 * MathF.PI / 180f);
        var shortSmoke = FlyThrow(smoke, RangeEye, 0, -0.3f);
        Line($"smoke at 40 deg: lands {s.firstDist:0.0} m out at {s.firstTime:0.00} s, pops at {s.endTime:0.00} s {s.end.Y:0.00} m up; short throw pops at {shortSmoke.endTime:0.00} s");
        if (s.end.Y > 0.6f)
            Fail($"a long smoke throw popped {s.end.Y:0.00} m in the air");
        if (MathF.Abs(shortSmoke.endTime - smoke.Fuse) > 0.02f)
            Fail($"a short smoke throw popped at {shortSmoke.endTime:0.00} s, not its {smoke.Fuse:0.0} s fuse");

        // The blurbs that name a distance match it.
        var said = System.Text.RegularExpressions.Regex.Match(frag.Blurb, @"throws about (\d+) m");
        if (!frag.Blurb.Contains($"{frag.Fuse:0.0} s fuse") || !said.Success || MathF.Abs(int.Parse(said.Groups[1].Value) - fragMax) > 1.5f)
            Fail($"frag blurb doesn't match the fuse / range: \"{frag.Blurb}\" (fuse {frag.Fuse:0.0} s, range {fragMax:0.0} m)");
    }

    /// <summary>Bots' throws (<see cref="Throwables.AimPitch"/>) go off near targets 10, 20 and 30 m away, in several directions.</summary>
    private static void GrenadeBotAim()
    {
        var worst = 0f;
        var parts = new List<string>();
        foreach (var kind in new[] { ThrowableKind.Frag, ThrowableKind.Impact, ThrowableKind.Smoke })
        {
            var def = Throwables.Get(kind);
            foreach (var range in new[] { 10f, 20f, 30f })
            {
                var miss = 0f;
                foreach (var yaw in new[] { 0f, 0.7f, -2.1f })
                {
                    var feet = new Vector3(0, 0, 10);
                    var eye = feet + new Vector3(0, World.EyeHeight, 0);
                    var target = feet + new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw)) * range;
                    var ok = Throwables.AimPitch(def, eye, target, out var pitch);
                    var (_, _, end, _) = FlyThrow(def, eye, MathF.Atan2(target.X - feet.X, -(target.Z - feet.Z)), pitch);
                    var m = new Vector2(end.X - target.X, end.Z - target.Z).Length();
                    miss = MathF.Max(miss, m);
                    if (!ok)
                        Fail($"bot aim: {def.Name} says {range} m is out of range");
                }
                parts.Add($"{def.Name} {range:0} m: {miss:0.00}");
                worst = MathF.Max(worst, miss);
                if (miss > 2f)
                    Fail($"bot aim: {def.Name} at {range:0} m goes off {miss:0.00} m from the target (limit 2 m)");
            }
        }
        Line($"bot throws go off within {worst:0.00} m of the target ({string.Join(", ", parts)} m)");

        // Out of range: says so and throws as far as it can.
        if (Throwables.AimPitch(Throwables.Get(ThrowableKind.Frag), RangeEye, RangeEye + new Vector3(0, -World.EyeHeight, -80), out var far) || far < 0.3f)
            Fail($"bot aim at 80 m: should report out of range and lob as far as it can (pitch {far:0.00})");
    }

    private static void GrenadeDeterminism()
    {
        var frag = Throwables.Get(ThrowableKind.Frag);
        var a = FlyThrow(frag, RangeEye, 0.3f, 0.5f);
        var b = FlyThrow(frag, RangeEye, 0.3f, 0.5f);
        Throwables.AimPitch(frag, RangeEye, new Vector3(5, 0, 50), out var p1);
        Throwables.AimPitch(frag, RangeEye, new Vector3(5, 0, 50), out var p2);
        // The bot's flat-ground prediction matches the real flight on a flat map.
        var dir = World.Forward(0.3f, 0.5f);
        var predicted = Throwables.PredictFlat(frag, Throwables.LaunchPoint(RangeEye, dir), Throwables.LaunchVelocity(frag, dir, Vector3.Zero), 0f, out _, out var pt);
        var gap = Vector3.Distance(predicted, a.end);
        Line($"determinism: same throw twice ends at {a.end} / {b.end}, aim {p1:0.0000} / {p2:0.0000}; flat prediction {gap:0.000} m from the real flight ({pt:0.00} s vs {a.endTime:0.00} s)");
        if (a.end != b.end || a.endTime != b.endTime || p1 != p2)
            Fail("grenade flight or bot aim isn't deterministic");
        if (gap > 0.05f)
            Fail($"the flat-ground prediction is {gap:0.000} m off the real flight");
    }
}
