using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Actuator sizes and the strength model (board item dev-sandbox-actuator-sizes-as01; docs/sandbox/CONSTRUCTS.md,
    /// "Actuator sizes and strength"): the size families load with what grows with size; a construct standing on piston
    /// legs holds its pose below its rating, awake, within a tiny tolerance; a lift loaded past its rating gives way by
    /// give x (load / rating - 1)^2, without bouncing, and comes back at its speed when the load goes; the size sets the
    /// range, the rating and the speed in play; the configure values clamp to the size's range and take effect; stacked
    /// pistons and a 10-joint arm stay finite and still (no explosions), with no allocation and the chain's tick cost.
    /// Short scripted scenarios on a flat test floor.
    /// </summary>
    private static void CheckConstructActuators()
    {
        ActuatorFamilies();
        ActuatorLegsHold();
        ActuatorYield();
        ActuatorSizesInPlay();
        ActuatorConfigure();
        ActuatorStacks();
    }

    private static readonly string[] ActuatorLengths = ["Short", "Medium", "Long"];

    /// <summary>The size family's part for width <paramref name="w"/> (1..3) and length <paramref name="l"/> (0..2): the old part where it is that size.</summary>
    private static ConstructPartDef? ActuatorSize(string family, int w, int l) =>
        ConstructParts.Find($"{family} {w}x{w} {ActuatorLengths[l]}") ?? (family, w, l) switch
        {
            ("Piston", 2, 2) => ConstructParts.Find("Piston"),
            ("Motor Hinge", 1, 1) => ConstructParts.Find("Motor Hinge"),
            ("Turntable", 2, 0) => ConstructParts.Find("Turntable"),
            _ => null,
        };

    private static float CfgMax(ConstructPartDef d, CfgKey k) => d.CfgSlot[(int)k] is var s and >= 0 ? d.Config[s].Max : float.NaN;
    private static float CfgMin(ConstructPartDef d, CfgKey k) => d.CfgSlot[(int)k] is var s and >= 0 ? d.Config[s].Min : float.NaN;

    /// <summary>Every family has its nine sizes; width raises the rating, length the stroke (pistons) or speed; mass, health and power grow with size.</summary>
    private static void ActuatorFamilies()
    {
        var bad = new List<string>();
        var parts = new List<string>();
        foreach (var family in new[] { "Piston", "Motor Hinge", "Turntable" })
        {
            var n = 0;
            for (var w = 1; w <= 3; w++)
                for (var l = 0; l < 3; l++)
                {
                    if (ActuatorSize(family, w, l) is not { } d)
                    {
                        bad.Add($"{family} {w}x{w} {ActuatorLengths[l]} is missing");
                        continue;
                    }
                    n++;
                    if (d.Role != PartRole.Joint || d.OverloadDamage != 0 || !(d.JointGive > 0))
                        bad.Add($"{d.Name}: not a joint, overload damage on, or no give");
                    if (!(CfgMin(d, CfgKey.Strength) > 0) || !(CfgMin(d, CfgKey.Speed) > 0) || d.MotorStrength > CfgMax(d, CfgKey.Strength) || d.MotorSpeed * (d.JointType == JointType.Piston ? 1 : 180 / MathF.PI) > CfgMax(d, CfgKey.Speed) + 0.01f)
                        bad.Add($"{d.Name}: its default strength or speed is outside its configure range");
                    if (w > 1 && ActuatorSize(family, w - 1, l) is { } thin)
                    {
                        if (!(CfgMax(d, CfgKey.Strength) > CfgMax(thin, CfgKey.Strength)))
                            bad.Add($"{d.Name}: no stronger than {thin.Name}");
                        if (d.JointType == JointType.Piston && !(d.SideLoad > thin.SideLoad))
                            bad.Add($"{d.Name}: takes no more side load than {thin.Name}");
                        if (!(d.Mass > thin.Mass && d.Health > thin.Health && d.PowerDraw > thin.PowerDraw) && d.Name.Contains('x') && thin.Name.Contains('x'))
                            bad.Add($"{d.Name}: not heavier, tougher and hungrier than {thin.Name}");
                    }
                    if (l > 0 && ActuatorSize(family, w, l - 1) is { } shorter)
                    {
                        var more = d.JointType == JointType.Piston ? d.JointMax > shorter.JointMax : CfgMax(d, CfgKey.Speed) > CfgMax(shorter, CfgKey.Speed);
                        if (!more)
                            bad.Add($"{d.Name}: {(d.JointType == JointType.Piston ? "no longer stroke" : "no faster")} than {shorter.Name}");
                        if (CfgMax(d, CfgKey.Strength) != CfgMax(shorter, CfgKey.Strength))
                            bad.Add($"{d.Name}: its length changed its rating");
                        if (!(d.Mass > shorter.Mass) && d.Name.Contains('x') && shorter.Name.Contains('x'))
                            bad.Add($"{d.Name}: not heavier than {shorter.Name}");
                    }
                }
            var lo = ActuatorSize(family, 1, 0);
            var hi = ActuatorSize(family, 3, 2);
            parts.Add($"{family}: {n} sizes, {lo?.Name} {CfgMax(lo!, CfgKey.Strength):0} to {hi?.Name} {CfgMax(hi!, CfgKey.Strength):0}, {lo?.Mass:0} to {hi?.Mass:0} kg");
        }
        Line($"actuator families: {string.Join("; ", parts)}");
        foreach (var b in bad)
            Fail($"construct actuators: {b}");
    }

    /// <summary>
    /// A free construct standing on piston legs: a frame beam along z, frame blocks out on both sides, a piston under the
    /// beam's ends and under each block (base up, extending down) and a frame block foot on each piston. Null if it doesn't build.
    /// </summary>
    internal static Construct? BuildPistonLegs(ConstructWorld w, IWorldQuery q, float x, float z, string piston = "Piston", string? knee = null)
    {
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Beam"), ConstructWorld.Ground(q, x, z) + new Vector3(0, 0.6f, 0), 0) is not { } c)
            return null;
        var def = ConstructParts.Need(piston);
        Func<byte, bool> down = rot => ConstructParts.DirVector(Rot24.ApplyDir(rot, def.JointAxis)).Y < -0.5f;
        var bl = FitTurned(w, c, 0, "x+1", "Frame Block", "-x", _ => true);
        var br = FitTurned(w, c, 0, "x-1", "Frame Block", "+x", _ => true);
        foreach (var (part, socket) in new[] { (0, "y-0"), (0, "y-3"), (bl, "-y"), (br, "-y") })
        {
            // (a knee: a motor hinge first, its leaf down and its axis flat, then the piston on its leaf)
            var k = knee == null || part < 0 ? part : FitTurned(w, c, part, socket, knee, "base", _ => true);
            var p = k < 0 ? -1 : FitTurned(w, c, k, knee == null ? socket : "leaf", piston, "base", down);
            if (p < 0 || FitTurned(w, c, p, "top", "Frame Block", "+y", _ => true) < 0)
            {
                w.Remove(c);
                return null;
            }
        }
        if (c.JointCount == (knee == null ? 4 : 8))
            return c;
        w.Remove(c);
        return null;
    }

    /// <summary>Sets a joint's part setting (clamped by its schema) and rebuilds; the value it took.</summary>
    private static float SetJointCfg(Construct c, int joint, CfgKey key, float value)
    {
        var part = c.Joints[joint].Hinge;
        var d = c.DefOf(part);
        ConstructConfig.Set(ref c.Parts[part], d, d.CfgSlot[(int)key], value);
        c.Dirty = true;
        c.Rebuild();
        return ConstructConfig.Get(c.Parts[part], d, key, float.NaN);
    }

    /// <summary>
    /// The developer's case: a construct on four piston legs pushed out 0.5 m, each set just above the load it carries,
    /// kept awake for 5 s: the body and the legs don't move (no sag, no jitter), and nothing allocates.
    /// </summary>
    private static void ActuatorLegsHold()
    {
        ActuatorLegsHold(null);
        ActuatorLegsHold("Motor Hinge 2x2 Short");
    }

    private static void ActuatorLegsHold(string? knee)
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (BuildPistonLegs(w, q, 0, 0, "Piston 2x2 Medium", knee) is not { } c)
        {
            Fail("construct actuators: the piston-legged construct doesn't build");
            return;
        }
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Slides)
            {
                SetJointCfg(c, j, CfgKey.Max, 0.5f);
                c.Joints[j].Open = true;
            }
        c.Wake();
        StepJoints(w, q, 180, _ => c.Wake());
        // Each leg's rating a quarter above the load it settled carrying (the tightest it can be and still hold).
        var ratio = new float[c.JointCount];
        for (var j = 0; j < c.JointCount; j++)
        {
            c.Joints[j].Strength = MathF.Max(c.Joints[j].Force * 1.25f, 500f);
            ratio[j] = c.Joints[j].Force / c.Joints[j].Strength;
        }
        StepJoints(w, q, 60, _ => c.Wake());
        var pos0 = c.Pos;
        var rot0 = c.Rot;
        var a0 = new float[c.JointCount];
        for (var j = 0; j < c.JointCount; j++)
            a0[j] = c.Joints[j].Angle;
        var worstY = 0f;
        var worstP = 0f;
        var worstA = 0f;
        var worstOver = 0f;
        var finite = true;
        Action<int> each = _ =>
        {
            c.Wake(); // (kept awake: a sleeping body would hold trivially)
            // Sag: the body's height and tilt (its far corner, 2 m out); creep: its far corner's move on its feet (the ground's friction).
            var dq = c.Rot * Quaternion.Conjugate(rot0);
            worstY = MathF.Max(worstY, MathF.Abs(c.Pos.Y - pos0.Y) + 2f * 2 * new Vector2(dq.X, dq.Z).Length());
            worstP = MathF.Max(worstP, Vector3.Distance(c.Pos, pos0) + 2f * 2 * new Vector3(dq.X, dq.Y, dq.Z).Length());
            for (var j = 0; j < c.JointCount; j++)
            {
                worstA = MathF.Max(worstA, MathF.Abs(c.Joints[j].Angle - a0[j]));
                worstOver = MathF.Max(worstOver, c.Joints[j].Over);
                finite &= float.IsFinite(c.Joints[j].Angle + c.Joints[j].Vel);
            }
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        StepJoints(w, q, 300, each);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        // Let be: it sleeps where it stands.
        var sleptAt = -1;
        var p1 = c.Pos;
        StepJoints(w, q, 180, t =>
        {
            if (sleptAt < 0 && c.Asleep)
                sleptAt = t;
        });
        var moved = sleptAt < 0 ? 1f : Vector3.Distance(c.Pos, p1) > 0.0005f ? Vector3.Distance(c.Pos, p1) : 0f;
        Line($"{(knee == null ? "piston legs" : $"two-joint legs ({knee} knees)")}: {c.Mass:0} kg on 4 pistons (2x2 medium) at {a0[c.JointCount - 1]:0.000} m, each rated a quarter over its load ({string.Join(", ", ratio.Select(r => $"{r * 100:0}%"))} of rating); "
             + $"awake for 5 s it sagged or tilted {worstY * 1000:0.000} mm, the legs moved {worstA * 1000:0.000} mm, its corners crept {worstP * 1000:0.000} mm on the ground's friction; "
             + $"the most load {worstOver * 100:0}% of rating; {bytes} bytes allocated; let be it slept after {sleptAt / 60f:0.00} s, {(moved < 1e-6f ? "still" : $"moved {moved * 1000:0.000} mm")}");
        if (!finite || worstY > 0.0002f || worstA > 0.0002f)
            Fail("construct actuators: a construct on piston legs below their rating didn't hold its pose (sag or jitter over 0.2 mm)");
        if (worstP > 0.005f)
            Fail("construct actuators: a construct on piston legs crept over 5 mm on the ground");
        if (sleptAt < 0 || moved > 1e-6f)
            Fail("construct actuators: a construct on piston legs at rest didn't sleep still");
        if (worstOver > 1)
            Fail("construct actuators: legs below their rating showed as overloaded");
        if (bytes > 0)
            Fail($"construct actuators: {bytes} bytes allocated in 300 ticks of a construct on piston legs (should be none)");
    }

    /// <summary>An anchored lift: a foundation, a piston of <paramref name="piston"/> on its middle and a deck plate on that.</summary>
    private static Construct? BuildTestLift(ConstructWorld w, IWorldQuery q, float x, float z, string piston)
    {
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, x, z), 0) is not { } c)
            return null;
        var p = FitTurned(w, c, 0, "top11", piston, "base", _ => true);
        if (p < 0 || FitTurned(w, c, p, "top", "Deck Plate", "bottom", _ => true) < 0 || c.JointCount != 1)
        {
            w.Remove(c);
            return null;
        }
        return c;
    }

    /// <summary>
    /// Past its rating a piston gives way by give x (load / rating - 1)^2, damped (no bounce), and comes back up at its
    /// speed when the load goes; the load against rating is what the panel's bar shows.
    /// </summary>
    private static void ActuatorYield()
    {
        var parts = new List<string>();
        var bad = false;
        foreach (var r in new[] { 1.25f, 1.5f, 2f })
        {
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (BuildTestLift(w, q, 0, 0, "Piston") is not { } c)
            {
                Fail("construct actuators: the test lift doesn't build");
                return;
            }
            ref var jt = ref c.Joints[0];
            SetJointCfg(c, 0, CfgKey.Max, 1.5f);
            jt.Open = true;
            StepJoints(w, q, 180);
            var load = MathF.Abs(jt.Load) + 4 * Construct.RiderMass * ConstructPhysics.GravityAccel;
            var top = jt.Angle;
            jt.Strength = load / r; // (below the configure range's floor: set on the joint for the test)
            var lowest = top;
            var expect = jt.Give * (r - 1) * (r - 1);
            StepJoints(w, q, 240, _ =>
            {
                c.RiderBits[1] = 15; // four riders on the deck
                lowest = MathF.Min(lowest, c.Joints[0].Angle);
            });
            var sag = top - jt.Angle;
            var over = jt.Over;
            var overshoot = jt.Angle - lowest;
            var hp0 = c.Parts[jt.Hinge].Health;
            if (r == 2f)
            {
                // A client hears the load (the panel's bar and the tint), and the panel's text says so.
                var client = new ConstructWorld();
                var pw = new PacketWriter(16384);
                ConstructNet.WriteFull(pw, c);
                var pr = new PacketReader(pw.Span[1..]);
                ConstructNet.Read(ref pr, client, 0);
                var cursor = 0;
                uint last = 0;
                c.Wake();
                ConstructNet.WritePoses(pw, w, 100, ref cursor);
                pr = new PacketReader(pw.Span[1..]);
                ConstructNet.ReadPoses(ref pr, client, 1.0, ref last);
                var copy = client.Get(c.Id);
                var text = Ui.SandboxLoadText(copy?.Joints[0].Over ?? 0, copy?.Joints[0].Strength ?? 0, true);
                parts.Add($"a client's copy shows \"{text}\"");
                if (copy == null || MathF.Abs(copy.Joints[0].Over - over) > 0.01f || !text.Contains("overloaded"))
                    Fail("construct actuators: a client's copy didn't hear an overloaded joint's load");
                // Overload damage is off (every shipped part): nothing taken off; turned on, the strain wears the part.
                var d = c.DefOf(jt.Hinge);
                var keep = d.OverloadDamage;
                try
                {
                    d.OverloadDamage = 40;
                    StepJoints(w, q, 60, _ => c.RiderBits[1] = 15);
                }
                finally
                {
                    d.OverloadDamage = keep;
                }
                var worn = hp0 - c.Parts[jt.Hinge].Health;
                parts.Add($"overload damage off: {hp0:0} hp kept; turned on (40 hp/s at 2x) a second took {worn:0} hp");
                if (hp0 != d.Health || worn < 20 || worn > 60)
                    Fail("construct actuators: overload damage hurt a part while off, or didn't when on");
            }
            // The load goes: back up at its speed.
            var back = -1;
            StepJoints(w, q, 180, t =>
            {
                if (back < 0 && c.Joints[0].Angle > top - 0.002f)
                    back = t;
            });
            var want = sag / jt.Speed * 60;
            parts.Add($"{r:0.00}x: sank {sag * 100:0.0} cm (curve {expect * 100:0.0} cm), {overshoot * 1000:0.0} mm below that at worst, shown at {over * 100:0}% of rating; back up in {back / 60f:0.00} s ({want / 60f:0.00} s at its speed)");
            bad |= MathF.Abs(sag - expect) > 0.2f * expect + 0.005f || overshoot > 0.01f + 0.05f * sag || MathF.Abs(over - r) > 0.1f * r || back < 0 || back > want + 30;
        }
        Line($"yield (Piston, {ConstructParts.Need("Piston").JointGive:0.##} m give, 1.5 m up, four riders): {string.Join("; ", parts)}");
        if (bad)
            Fail("construct actuators: an overloaded piston didn't give way along its curve (or bounced, or didn't come back at its speed)");
    }

    /// <summary>The size in play: a short piston's stroke stops at its own; a thin piston gives under a load a thick one holds; a long hinge turns faster than a short one.</summary>
    private static void ActuatorSizesInPlay()
    {
        var bad = new List<string>();
        // Range: each length's lift pushed out fully.
        var reach = new List<string>();
        for (var l = 0; l < 3; l++)
        {
            var name = ActuatorSize("Piston", 1, l)!.Name;
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (BuildTestLift(w, q, 0, 0, name) is not { } c)
            {
                bad.Add($"a lift on a {name} doesn't build");
                continue;
            }
            c.Joints[0].Open = true;
            StepJoints(w, q, 360);
            reach.Add($"{name} {c.Joints[0].Angle:0.00} m");
            if (MathF.Abs(c.Joints[0].Angle - c.DefOf(c.Joints[0].Hinge).JointMax) > 0.01f)
                bad.Add($"a {name} didn't push out to its {c.DefOf(c.Joints[0].Hinge).JointMax} m stroke");
        }
        // Force: the same four riders on a 1x1 and a 3x3 piston (at their full ratings).
        var force = new List<string>();
        var sags = new float[2];
        for (var k = 0; k < 2; k++)
        {
            var name = ActuatorSize("Piston", k == 0 ? 1 : 3, 1)!.Name;
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (BuildTestLift(w, q, 0, 0, name) is not { } c)
            {
                bad.Add($"a lift on a {name} doesn't build");
                continue;
            }
            c.Joints[0].Open = true;
            StepJoints(w, q, 240);
            var top = c.Joints[0].Angle;
            StepJoints(w, q, 240, _ => c.RiderBits[1] = 0xFF); // eight riders
            sags[k] = top - c.Joints[0].Angle;
            force.Add($"{name} ({c.Joints[0].Strength:0} N) sank {sags[k] * 100:0.0} cm at {c.Joints[0].Over * 100:0}%");
        }
        if (!(sags[0] > 0.02f) || sags[1] > 0.001f)
            bad.Add("eight riders didn't sink a 1x1 piston, or sank a 3x3 one");
        // Speed: a short and a long 1x1 motor hinge on a block, opened from 0 to 90 degrees.
        var speed = new List<string>();
        var ticks = new int[2];
        for (var k = 0; k < 2; k++)
        {
            var name = ActuatorSize("Motor Hinge", 1, k == 0 ? 0 : 2)!.Name;
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            var c = ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), Vector3.Zero, 0);
            var b0 = c == null ? -1 : FitTurned(w, c, 0, "top11", "Frame Block", "-y", _ => true);
            var b = b0 < 0 ? -1 : FitTurned(w, c!, b0, "+y", "Frame Block", "-y", _ => true); // (two blocks up: a long hinge clears the foundation)
            var h = b < 0 ? -1 : FitTurned(w, c!, b, "+x", name, "base", rot => MathF.Abs(ConstructParts.DirVector(Rot24.ApplyDir(rot, ConstructParts.Need(name).JointAxis)).Y) > 0.5f);
            if (c == null || h < 0 || FitTurned(w, c, h, "leaf", "Frame Block", "-x", _ => true) < 0)
            {
                bad.Add($"a test arm on a {name} doesn't build");
                continue;
            }
            SetJointCfg(c, 0, CfgKey.Max, 90);
            c.Joints[0].Open = true;
            ticks[k] = -1;
            StepJoints(w, q, 300, t =>
            {
                if (ticks[k] < 0 && c.Joints[0].Angle > 89.5f * MathF.PI / 180)
                    ticks[k] = t;
            });
            speed.Add($"{name} ({Deg(c.Joints[0].Speed):0} deg/s) took {ticks[k] / 60f:0.00} s");
        }
        if (ticks[0] < 0 || ticks[1] < 0 || !(ticks[1] * 2 < ticks[0]))
            bad.Add("a long motor hinge didn't turn much faster than a short one");
        Line($"sizes in play: stroke {string.Join(", ", reach)}; force: {string.Join("; ", force)}; speed: 0 to 90 deg, {string.Join("; ", speed)}");
        foreach (var x in bad)
            Fail($"construct actuators: {x}");
    }

    /// <summary>The configure tool's Strength and Speed clamp to the size's range and take effect on the joint.</summary>
    private static void ActuatorConfigure()
    {
        var bad = new List<string>();
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        var name = ActuatorSize("Piston", 1, 1)!.Name;
        if (BuildTestLift(w, q, 0, 0, name) is not { } c)
        {
            Fail("construct actuators: the configure test lift doesn't build");
            return;
        }
        var d = c.DefOf(c.Joints[0].Hinge);
        float sMin = CfgMin(d, CfgKey.Strength), sMax = CfgMax(d, CfgKey.Strength), vMin = CfgMin(d, CfgKey.Speed), vMax = CfgMax(d, CfgKey.Speed);
        var hi = SetJointCfg(c, 0, CfgKey.Strength, sMax * 10);
        var lo = SetJointCfg(c, 0, CfgKey.Strength, 0);
        var fast = SetJointCfg(c, 0, CfgKey.Speed, 100);
        var slow = SetJointCfg(c, 0, CfgKey.Speed, 0);
        if (hi != sMax || lo != sMin || fast != vMax || slow != vMin)
            bad.Add($"the {name}'s strength or speed setting didn't clamp to its size's range ({hi}, {lo}, {fast}, {slow})");
        // Take effect: the weakest strength gives under riders the strongest holds; the speed sets how fast it goes out.
        var sank = new float[2];
        var outT = new int[2];
        for (var k = 0; k < 2; k++)
        {
            SetJointCfg(c, 0, CfgKey.Strength, sMax);
            SetJointCfg(c, 0, CfgKey.Speed, k == 0 ? vMax : vMax / 4);
            if (MathF.Abs(c.Joints[0].Speed - (k == 0 ? vMax : vMax / 4)) > 1e-4f)
                bad.Add("a configured speed didn't reach the joint");
            c.Joints[0].Open = false;
            StepJoints(w, q, 240);
            c.Joints[0].Open = true;
            c.Wake();
            outT[k] = -1;
            StepJoints(w, q, 360, t =>
            {
                if (outT[k] < 0 && c.Joints[0].Angle > c.Joints[0].Max - 0.01f)
                    outT[k] = t;
            });
            var top = c.Joints[0].Angle;
            SetJointCfg(c, 0, CfgKey.Strength, k == 0 ? sMax : sMin);
            if (c.Joints[0].Strength != (k == 0 ? sMax : sMin))
                bad.Add("a configured strength didn't reach the joint");
            StepJoints(w, q, 180, _ => c.RiderBits[1] = 3); // two riders
            sank[k] = top - c.Joints[0].Angle;
        }
        Line($"configure ({name}): strength {sMin:0}..{sMax:0} N, speed {vMin:0.##}..{vMax:0.##} m/s (asked 10x, 0, 100, 0: took {hi:0}, {lo:0}, {fast:0.##}, {slow:0.##}); "
             + $"at full strength two riders sank it {sank[0] * 100:0.0} cm, at the least {sank[1] * 100:0.0} cm; out at full speed in {outT[0] / 60f:0.00} s, a quarter in {outT[1] / 60f:0.00} s");
        if (sank[0] > 0.001f || !(sank[1] > 0.02f))
            bad.Add("the configured strength didn't change what the piston holds");
        if (outT[0] < 0 || outT[1] < 0 || !(outT[1] > outT[0] * 3))
            bad.Add("the configured speed didn't change how fast the piston goes out");
        foreach (var x in bad)
            Fail($"construct actuators: {x}");
    }

    /// <summary>
    /// Stacked actuators: a tower of six pistons and an upright arm of ten motor hinges, worked by seeded inputs and
    /// overloaded: everything stays finite, nothing flies off, it holds still once settled, no allocation, and the
    /// ten-joint arm's tick cost.
    /// </summary>
    private static void ActuatorStacks()
    {
        var bad = new List<string>();
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        // A tower: six 1x1 medium pistons stacked on a foundation, a frame block on top.
        var tower = ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), new Vector3(-4, 0, 0), 0);
        var on = 0;
        var socket = "top11";
        for (var k = 0; k < 6 && tower != null && on >= 0; k++)
        {
            on = FitTurned(w, tower, on, socket, "Piston 1x1 Medium", "base", _ => true);
            socket = "top";
        }
        if (tower == null || on < 0 || FitTurned(w, tower, on, "top", "Frame Block", "-y", _ => true) < 0 || tower.JointCount != 6)
        {
            Fail("construct actuators: the piston tower doesn't build");
            return;
        }
        // An arm: ten 2x2 medium motor hinges upright on a foundation, a frame block between each (axes along x).
        var arm = ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), new Vector3(4, 0, 0), 0);
        var hingeDef = ConstructParts.Need("Motor Hinge 2x2 Medium");
        Func<byte, bool> alongX = rot => MathF.Abs(ConstructParts.DirVector(Rot24.ApplyDir(rot, hingeDef.JointAxis)).X) > 0.5f;
        on = arm == null ? -1 : FitTurned(w, arm, 0, "top11", "Frame Block", "-y", _ => true);
        for (var k = 0; k < 10 && on >= 0; k++)
        {
            var h = FitTurned(w, arm!, on, "+y", hingeDef.Name, "base", alongX);
            on = h < 0 ? -1 : FitTurned(w, arm!, h, "leaf", "Frame Block", "-y", _ => true);
        }
        if (arm == null || on < 0 || arm.JointCount != 10)
        {
            Fail($"construct actuators: the ten-hinge arm doesn't build ({arm?.JointCount ?? 0} joints)");
            return;
        }
        for (var j = 0; j < arm.JointCount; j++)
        {
            SetJointCfg(arm, j, CfgKey.Min, -15);
            SetJointCfg(arm, j, CfgKey.Max, 15);
        }
        // Ten joints travel to a client (a construct may have 16).
        {
            var client = new ConstructWorld();
            var pw = new PacketWriter(65536);
            ConstructNet.WriteFull(pw, arm);
            var pr = new PacketReader(pw.Span[1..]);
            ConstructNet.Read(ref pr, client, 0);
            if (client.Get(arm.Id) is not { JointCount: 10 })
                bad.Add("a client's copy of the ten-hinge arm didn't get its ten joints");
        }
        var rng = new Random(1234);
        var finite = true;
        var fastest = 0f;
        var furthest = 0f;
        void Watch(Construct c)
        {
            for (var j = 0; j < c.JointCount; j++)
            {
                finite &= float.IsFinite(c.Joints[j].Angle + c.Joints[j].Vel + c.Joints[j].Force);
                fastest = MathF.Max(fastest, MathF.Abs(c.Joints[j].Vel));
            }
            for (var i = 0; i < c.PartCount; i++)
                furthest = MathF.Max(furthest, Vector3.Distance(ConstructRender.PartCentre(c, i), c.Pos));
        }
        // Seeded inputs: each joint latched open or shut at random every half second, the tower overloaded now and then.
        StepJoints(w, q, 600, t =>
        {
            if (t % 30 == 0)
            {
                for (var j = 0; j < tower.JointCount; j++)
                    tower.Joints[j].Open = rng.Next(2) == 0;
                for (var j = 0; j < arm.JointCount; j++)
                    arm.Joints[j].Open = rng.Next(3) != 0;
                tower.Wake();
                arm.Wake();
            }
            if (t % 120 >= 60)
                tower.RiderBits[6] = 0xFFFF; // sixteen riders on the top block: past the pistons' ratings
            Watch(tower);
            Watch(arm);
        });
        // Settled with the arm bent: it holds still (awake), no allocation, and its tick cost.
        for (var j = 0; j < arm.JointCount; j++)
            arm.Joints[j].Open = j % 2 == 0;
        StepJoints(w, q, 120, _ => arm.Wake());
        for (var j = 0; j < arm.JointCount; j++)
            arm.Joints[j].Strength = MathF.Max(arm.Joints[j].Strength, 1.5f * MathF.Abs(arm.Joints[j].Load)); // (below every rating: set on the joints for the test)
        StepJoints(w, q, 120, _ => arm.Wake());
        var tip0 = ConstructRender.PartCentre(arm, arm.PartCount - 1);
        var drift = 0f;
        Action<int> hold = _ =>
        {
            arm.Wake();
            drift = MathF.Max(drift, Vector3.Distance(ConstructRender.PartCentre(arm, arm.PartCount - 1), tip0));
            Watch(arm);
        };
        Action<int> wakeArm = _ => arm.Wake();
        hold(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        StepJoints(w, q, 120, hold);
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds / 120;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        // The arm alone: its own tick cost.
        tower.Asleep = true;
        var alone = System.Diagnostics.Stopwatch.StartNew();
        StepJoints(w, q, 120, wakeArm);
        var msArm = alone.Elapsed.TotalMilliseconds / 120;
        var maxLoad = 0f;
        for (var j = 0; j < arm.JointCount; j++)
            maxLoad = MathF.Max(maxLoad, arm.Joints[j].Over);
        Line($"stacked: six pistons and a ten-hinge arm worked by seeded inputs for 10 s (the tower overloaded every other second): finite {finite}, fastest joint {fastest:0.00} a second, "
             + $"furthest part {furthest:0.0} m from its body; the bent arm held its tip within {drift * 1000:0.000} mm (the most loaded joint at {maxLoad * 100:0}%), "
             + $"{bytes} bytes allocated in 120 ticks; {ms:0.000} ms a tick for both, {msArm:0.000} ms for the ten-joint arm alone");
        if (!finite || fastest > 20 || furthest > 12)
            bad.Add("stacked actuators blew up (a NaN, a joint spinning wild or a part flung off)");
        if (drift > 0.001f)
            bad.Add("a ten-joint arm below its ratings didn't hold its tip still");
        if (bytes > 0)
            bad.Add($"{bytes} bytes allocated in 120 ticks of stacked actuators (should be none)");
        if (msArm > 1.0)
            bad.Add($"{msArm:0.00} ms a tick for a ten-joint arm (budget 1 ms of the 8 ms tick)");
        foreach (var x in bad)
            Fail($"construct actuators: {x}");
    }
}
