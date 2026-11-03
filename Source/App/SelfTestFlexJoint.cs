using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Flexible joints" (ConstructFlex.cs, ConstructFlexChute.cs; docs/sandbox/CONSTRUCTS.md, "Flexible joints" and "Flexible
    /// chutes"), in real physics on a flat floor. The parts: sizes, slots, no power. A weighted arm bends to where its
    /// stiffness says, to its limit under a heavy load, and comes back; bends any way inside a circle; a ball joint twists, a
    /// rubber joint doesn't. It doesn't diverge: a wrecking ball, seeded kicks, the softest and stiffest settings, a free light
    /// body. Welded on and off the grid and freeform, it behaves as the wrench's. A motor hinge beyond it works, a wheel beyond
    /// it rides the ground. A player stands on the arm with both movers. A signal locks it. A chute across a moving hinge,
    /// piston, turntable and flexible joint carries bulk and parts, stops at its limit with the reason, comes back, tears past
    /// it and is mended. A cargo hose moves loads between two constructs and pops out past its length. Nothing allocates.
    /// </summary>
    private static void CheckFlexJoints()
    {
        FlexParts();
        FlexArm();
        FlexAnyWay();
        FlexHeavy();
        FlexWelds();
        FlexBeyond();
        FlexRiders();
        FlexChuteHinge();
        FlexChuteLimits();
        FlexCargoHose();
        FlexBudget();
    }

    private const float FxDeg = MathF.PI / 180;

    /// <summary>A pinned foundation with a frame block on it: a flexible joint on the block's +x side, its leaf out along +x, and a frame beam on the leaf. -1 parts when it doesn't fit.</summary>
    private static Construct? FxArm(ConstructWorld w, IWorldQuery q, string joint, out int jp, out int beam, float x = 0, float z = 0, bool withBeam = true)
    {
        jp = beam = -1;
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, x, z) + new Vector3(0, 1.5f, 0), 0) is not { } c)
            return null;
        c.Pinned = true;
        var block = FitTurned(w, c, 0, "top22", "Frame Block", "-y", _ => true);
        var d = ConstructParts.Need(joint);
        jp = block < 0 ? -1 : FitTurned(w, c, block, "+x", joint, "base", _ => true);
        if (jp < 0)
            return null;
        if (withBeam)
            beam = FitTurned(w, c, jp, d.LeafSocket, "Frame Beam", "end-", _ => true);
        c.Rebuild();
        return c;
    }

    private static void FxStep(ConstructWorld w, IWorldQuery q, int ticks)
    {
        for (var t = 0; t < ticks; t++)
            w.Step(q, World.TickDt);
    }

    private static float FxBend(Construct c, int jp) => c.FlexBend(jp, out _);

    private static float FxPivotGap(Construct c, int jp, int leafPart)
    {
        // How far the leaf part's socket end is from the pivot: a joint that "came apart" shows here.
        c.WorldFrame(c.Parts[leafPart].Seg, out var q, out var t);
        var j = c.JointOf(jp);
        c.WorldFrame(c.Joints[j].ParentSeg, out var pq, out var pt);
        return Vector3.Distance(t + Vector3.Transform(c.Parts[leafPart].Pos, q), pt + Vector3.Transform(c.Joints[j].Pivot, pq));
    }

    private static void FlexParts()
    {
        var n = 0;
        var strongest = 0f;
        foreach (var kind in new[] { "Flex Joint", "Ball Joint" })
        {
            var last = 0f;
            foreach (var size in new[] { "1x1", "2x2", "3x3" })
            {
                var d = ConstructParts.Need($"{kind} {size}");
                n++;
                if (d.JointType != JointType.Flex || d.Electric || d.PowerDraw != 0 || d.LeafIndex < 0 || !d.Boxes.Any(b => b.IsLeaf) || !d.Boxes.Any(b => b.IsBase))
                    Fail($"flexible joints: the {d.Name} isn't a passive joint with two sides");
                if (d.MotorStrength <= last)
                    Fail($"flexible joints: the {d.Name} isn't rated above the size below it");
                last = d.MotorStrength;
                strongest = MathF.Max(strongest, last);
                if (d.JointSlots != (kind == "Ball Joint" ? 3 : 2))
                    Fail($"flexible joints: the {d.Name} takes {d.JointSlots} joints");
            }
        }
        // The cap counts them, and says so.
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        string? refusal = null;
        if (FxArm(w, q, "Ball Joint 1x1", out _, out _, withBeam: false) is { } c)
        {
            c.JointCount = Construct.MaxJoints - 2;
            refusal = ConstructBuild.LeafRefusal(c, c.PartCount - 1);
            c.JointCount = 0;
        }
        Line($"flexible joints: {n} parts, up to {strongest:0} N m; at the cap: \"{refusal}\"");
        if (refusal == null || !refusal.Contains("flexible joint"))
            Fail("flexible joints: a construct with no room left for a ball joint's three joints wasn't refused with the reason");
    }

    /// <summary>A beam on a rubber joint droops where its stiffness says; a wrecking ball takes it to its stop; taken off, it comes back without much overshoot.</summary>
    private static void FlexArm()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (FxArm(w, q, "Flex Joint 1x1", out var jp, out var beam) is not { } c || beam < 0)
        {
            Fail("flexible joints: the arm doesn't build");
            return;
        }
        var d = c.DefOf(jp);
        var head = c.JointOf(jp);
        var leafSeg = c.LeafSegOf(head);
        if (head < 0 || c.Parts[beam].Seg != leafSeg || leafSeg != head + 2 || !c.Under(beam, head + 1) || c.MoveSeg(c.Parts[beam].Seg) != leafSeg)
            Fail($"flexible joints: the beam isn't on the joint's last segment (joint {head}, segment {c.Parts[beam].Seg})");
        c.Wake();
        FxStep(w, q, 240);
        var droop = FxBend(c, jp);
        var expect = (c.Mass > 0 ? MathF.Abs(c.Joints[head].Load) + MathF.Abs(c.Joints[head + 1].Load) : 0) / (d.FlexStiffness);
        var over0 = c.Joints[head].Over;
        // A wrecking ball on its end: past its rating, on its stop.
        var ball = FitTurned(w, c, beam, "end+", "Wrecking Ball", "top", _ => true);
        c.Wake();
        FxStep(w, q, 180);
        var loaded = FxBend(c, jp);
        var over = c.Joints[head].Over;
        var gap = FxPivotGap(c, jp, beam);
        // It goes: the arm comes back.
        if (ball >= 0)
            w.RemovePart(c, ball);
        c.Wake();
        var least = loaded;
        for (var t = 0; t < 240; t++)
        {
            w.Step(q, World.TickDt);
            least = MathF.Min(least, FxBend(c, jp));
        }
        var back = FxBend(c, jp);
        Line($"flexible joints, arm: droops {Deg(droop):0.0} deg (load / stiffness {Deg(expect):0.0}) at {over0 * 100:0}% of its rating; a wrecking ball takes it to {Deg(loaded):0.0} of {Deg(d.JointMax):0} deg at {over * 100:0}%, still {gap * 100:0.0} cm off its pivot; "
             + $"without it back to {Deg(back):0.0} (at most {Deg(droop - least):0.0} past where it hung)");
        if (!(droop > 2 * FxDeg) || MathF.Abs(droop - expect) > 1.5f * FxDeg)
            Fail($"flexible joints: the arm droops {Deg(droop):0.0} deg, its load over its stiffness says {Deg(expect):0.0}");
        if (ball < 0 || MathF.Abs(loaded - d.JointMax) > 0.5f * FxDeg || !(over > 1))
            Fail($"flexible joints: a heavy load took it to {Deg(loaded):0.0} deg (limit {Deg(d.JointMax):0}), {over * 100:0}% of its rating");
        if (MathF.Abs(back - droop) > 0.5f * FxDeg || droop - least > 4 * FxDeg)
            Fail($"flexible joints: the arm came back to {Deg(back):0.0} deg (it hung at {Deg(droop):0.0}), overshooting {Deg(droop - least):0.0}");
        // The panel's Load bar and the settings: softer droops further, a signal locks it where it is.
        var slot = Array.FindIndex(d.Config, e => e.Id == CfgKey.Stiffness);
        ConstructConfig.Set(ref c.Parts[jp], d, slot, d.Config[slot].Min);
        c.Rebuild();
        c.Wake();
        FxStep(w, q, 240);
        var soft = FxBend(c, jp);
        var lever = FitTurned(w, c, 0, "top00", "Lever", "-y", _ => true);
        var wired = lever >= 0 && w.AddWire(c, lever, jp);
        c.Parts[lever].State = 1;
        c.Wake();
        FxStep(w, q, 3);
        var at = FxBend(c, jp);
        c.Joints[head].Angle += 0.05f;
        var locked = c.Joints[head].Locked;
        FxStep(w, q, 60);
        var held = FxBend(c, jp);
        Line($"flexible joints, settings: a tenth of the stiffness droops {Deg(soft):0.0} deg; a signal locks it at {Deg(at):0.0} and it stays at {Deg(held):0.0}");
        if (!(soft > droop) || !wired || !locked || MathF.Abs(held - at) > 0.1f)
            Fail("flexible joints: the stiffness setting or the lock doesn't work");
    }

    /// <summary>It bends any way and stays inside a circle, not a square; a ball joint twists and a rubber joint doesn't; a shove swings it.</summary>
    private static void FlexAnyWay()
    {
        foreach (var name in new[] { "Flex Joint 2x2", "Ball Joint 2x2" })
        {
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (FxArm(w, q, name, out var jp, out var beam) is not { } c)
            {
                Fail($"flexible joints: the {name} arm doesn't build");
                return;
            }
            var d = c.DefOf(jp);
            var j = c.JointOf(jp);
            var most = 0f;
            var turned = 0f;
            var rng = new Random(11);
            for (var kick = 0; kick < 8; kick++)
            {
                c.Wake();
                var a = kick * MathF.PI / 4 + 0.3f;
                c.Joints[j].Vel = MathF.Cos(a) * 9;
                c.Joints[j + 1].Vel = MathF.Sin(a) * 9;
                if (d.JointSlots == 3)
                    c.Joints[j + 2].Vel = (float)rng.NextDouble() * 18 - 9;
                for (var t = 0; t < 40; t++)
                {
                    w.Step(q, World.TickDt);
                    most = MathF.Max(most, FxBend(c, jp));
                    if (d.JointSlots == 3)
                        turned = MathF.Max(turned, MathF.Abs(c.Joints[j + 2].Angle));
                }
            }
            // A shove on the beam from the side moves more than one way.
            FxStep(w, q, 240);
            var b0 = FxBend(c, jp);
            var ax0 = c.Joints[j].Angle;
            var ay0 = c.Joints[j + 1].Angle;
            var tip = c.ToWorld(c.Parts[beam].Pos);
            ConstructWorld.PushJoint(c, c.Parts[beam].Seg, tip, new Vector3(0, 400, 0));
            FxStep(w, q, 6);
            var moved = MathF.Abs(c.Joints[j].Angle - ax0) + MathF.Abs(c.Joints[j + 1].Angle - ay0);
            Line($"flexible joints, any way: the {name} reached {Deg(most):0.0} of {Deg(d.JointMax):0} deg bent, {Deg(turned):0.0} of {Deg(d.FlexTwist):0} twisted; hung at {Deg(b0):0.0}, a shove moved it {Deg(moved):0.0}");
            if (most > d.JointMax * 1.005f || !(most > d.JointMax * 0.9f))
                Fail($"flexible joints: the {name} bent {Deg(most):0.0} deg with a cone of {Deg(d.JointMax):0}");
            if (d.JointSlots == 3 && (turned > d.FlexTwist * 1.005f || !(turned > d.FlexTwist * 0.5f)))
                Fail($"flexible joints: the {name} twisted {Deg(turned):0.0} deg, limit {Deg(d.FlexTwist):0}");
            if (!(moved > 0.2f * FxDeg))
                Fail($"flexible joints: a shove on the {name}'s beam didn't move it");
        }
    }

    /// <summary>A wrecking ball on every size at both ends of its stiffness, kicked at random; a light free body carrying one: everything stays finite, bounded and in one piece.</summary>
    private static void FlexHeavy()
    {
        var worstSpin = 0f;
        var cases = 0;
        foreach (var name in new[] { "Flex Joint 1x1", "Ball Joint 1x1", "Flex Joint 3x3", "Ball Joint 3x3" })
            foreach (var stiff in new[] { false, true })
            {
                var w = new ConstructWorld();
                var q = new JointTestWorld();
                if (FxArm(w, q, name, out var jp, out var beam) is not { } c || FitTurned(w, c, beam, "end+", "Wrecking Ball", "top", _ => true) < 0)
                {
                    Fail($"flexible joints: the {name} with a wrecking ball doesn't build");
                    return;
                }
                var d = c.DefOf(jp);
                var slot = Array.FindIndex(d.Config, e => e.Id == CfgKey.Stiffness);
                ConstructConfig.Set(ref c.Parts[jp], d, slot, stiff ? d.Config[slot].Max : d.Config[slot].Min);
                c.Rebuild();
                var j = c.JointOf(jp);
                var gap0 = FxPivotGap(c, jp, beam);
                var rng = new Random(23);
                var bad = "";
                for (var t = 0; t < 300 && bad.Length == 0; t++)
                {
                    if (t % 40 == 0)
                    {
                        c.Wake();
                        for (var k = 0; k < d.JointSlots; k++)
                            c.Joints[j + k].Vel += (float)rng.NextDouble() * 24 - 12;
                    }
                    w.Step(q, World.TickDt);
                    var bend = FxBend(c, jp);
                    for (var k = 0; k < d.JointSlots; k++)
                    {
                        if (!float.IsFinite(c.Joints[j + k].Angle + c.Joints[j + k].Vel))
                            bad = "not a number";
                        worstSpin = MathF.Max(worstSpin, MathF.Abs(c.Joints[j + k].Vel));
                    }
                    if (bend > d.JointMax * 1.01f)
                        bad = $"bent {Deg(bend):0.0} of {Deg(d.JointMax):0}";
                    if (MathF.Abs(FxPivotGap(c, jp, beam) - gap0) > 0.01f)
                        bad = "came apart";
                }
                cases++;
                if (bad.Length > 0)
                    Fail($"flexible joints: a {name} ({(stiff ? "stiffest" : "softest")}) with a wrecking ball: {bad}");
                // Left alone, it ends up still on its stop, or where the spring meets the load.
                FxStep(w, q, 240);
                if (c.Joints[j].Vel * c.Joints[j].Vel + c.Joints[j + 1].Vel * c.Joints[j + 1].Vel > 0.05f)
                    Fail($"flexible joints: a {name} ({(stiff ? "stiffest" : "softest")}) with a wrecking ball doesn't settle");
            }
        // A free 30 kg block carrying a wrecking ball on a ball joint, dropped and shaken.
        var fw = new ConstructWorld();
        var fq = new JointTestWorld();
        float topSpeed = 0, topSpin = 0;
        var fine = false;
        if (ConstructBuild.PlaceNew(fw, ConstructParts.Need("Frame Block"), new Vector3(0, 2.5f, 0), 0) is { } free
            && FitTurned(fw, free, 0, "+x", "Ball Joint 1x1", "base", _ => true) is var fjp and >= 0
            && FitTurned(fw, free, fjp, "top", "Wrecking Ball", "top", _ => true) >= 0)
        {
            fine = true;
            var r2 = new Random(5);
            var fj = free.JointOf(fjp);
            for (var t = 0; t < 360; t++)
            {
                if (t % 30 == 0)
                {
                    free.Wake();
                    free.Joints[fj].Vel += (float)r2.NextDouble() * 16 - 8;
                }
                fw.Step(fq, World.TickDt);
                topSpeed = MathF.Max(topSpeed, free.Vel.Length());
                topSpin = MathF.Max(topSpin, free.AngVel.Length());
                fine &= float.IsFinite(free.Pos.X + free.Pos.Y + free.Pos.Z);
            }
        }
        Line($"flexible joints, heavy: {cases} wrecking-ball cases, top joint speed {worstSpin:0.0} rad/s; a free block under a ball joint and a wrecking ball: top speed {topSpeed:0.0} m/s, spin {topSpin:0.0} rad/s");
        if (!fine || topSpeed > 12 || topSpin > 30)
            Fail($"flexible joints: a free light body under a heavy load went wild ({topSpeed:0.0} m/s, {topSpin:0.0} rad/s)");
    }

    /// <summary>A panel on the joint's leaf by the wrench, by a socket weld on and off the grid, and by a freeform weld: it hangs on the joint's last segment and springs the same.</summary>
    private static void FlexWelds()
    {
        const string name = "Ball Joint 1x1";
        var pieceDef = ConstructParts.Need("Frame Block");
        var pieceSock = SwSocket(pieceDef, "-y");
        var results = new List<string>();
        float reference = -1;
        foreach (var how in new[] { "wrench", "socket weld", "socket weld +30", "freeform weld" })
        {
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (FxArm(w, q, name, out var jp, out _, withBeam: false) is not { } c)
            {
                Fail("flexible joints: the weld rig doesn't build");
                return;
            }
            var d = c.DefOf(jp);
            var leaf = d.LeafIndex;
            string? why = null;
            if (ConstructBuild.Place(c, jp, leaf, pieceDef, pieceSock, 0, out var g) is { } refused)
            {
                Fail($"flexible joints: a frame block doesn't go on the leaf: {refused}");
                return;
            }
            JwLeaf(c, jp, out var leafAt, out var leafDir);
            var eye = leafAt + new Vector3(0.6f, 1f, 1f);
            var hands = FwHands();
            int piece;
            if (how == "wrench")
                piece = ConstructBuild.Attach(w, c, pieceDef, g);
            else if (how == "freeform weld")
            {
                if (FwLoose(w, "Frame Block", Vector3.Zero, Quaternion.Identity, frozen: true) is not { } loose)
                    return;
                c.WorldFrame(c.Parts[jp].Seg, out var cq, out var ct);
                loose.Pos = loose.PrevPos = ct + Vector3.Transform(g.Pos, cq) + leafDir * 0.02f;
                loose.Rot = loose.PrevRot = Quaternion.Normalize(cq * Rot24.Quat(g.Rot));
                why = hands.BeginFreeform(w, 0, eye, loose.Id, 0, c.Id, jp, 0);
                c.Rebuild();
                piece = c.PartCount - 1;
            }
            else
            {
                if (FwLoose(w, "Frame Block", leafAt + new Vector3(1.8f, 0.8f, -0.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), frozen: true) is not { } loose)
                    return;
                var target = new WeldTarget { Body = c.Id, Part = (byte)jp, Socket = (byte)leaf };
                SocketWeld.SourceFrame(loose, 0, pieceSock, out _, out var sDir);
                SocketWeld.TargetFrame(c, target, out _, out var tDir);
                var degrees = SocketWeld.FromWire(SocketWeld.WireAngle(SocketWeld.AngleOfPartTurn(loose, 0, sDir, tDir, g.Rot) + (how == "socket weld" ? 0 : 30)));
                why = hands.BeginSocketWeld(w, 0, eye, loose.Id, 0, pieceSock, target, degrees, 0);
                c.Rebuild();
                piece = c.PartCount - 1;
            }
            var j = c.JointOf(jp);
            if (why != null || piece < 0 || j < 0 || !c.Under(piece, j + 1) || c.MoveSeg(c.Parts[piece].Seg) == 0)
            {
                Fail($"flexible joints: {how} onto the {name}'s leaf: {why ?? "it isn't beyond the joint"}");
                continue;
            }
            // Driven: it droops, then it is swung and comes back.
            c.Wake();
            FxStep(w, q, 240);
            var hang = FxBend(c, jp);
            c.Joints[j].Vel = 6;
            c.Joints[j + 1].Vel = -6;
            c.Wake();
            var peak = 0f;
            for (var t = 0; t < 30; t++)
            {
                w.Step(q, World.TickDt);
                peak = MathF.Max(peak, FxBend(c, jp));
            }
            FxStep(w, q, 240);
            var back = FxBend(c, jp);
            results.Add($"{how} hangs {Deg(hang):0.00} deg, swings to {Deg(peak):0.0}, back at {Deg(back):0.00}");
            if (reference < 0)
                reference = hang;
            else if (MathF.Abs(hang - reference) > 1.5f * FxDeg)
                Fail($"flexible joints: the {how} hangs {Deg(hang):0.00} deg, the wrench's {Deg(reference):0.00}");
            if (!(peak > hang + 5 * FxDeg) || MathF.Abs(back - hang) > 0.5f * FxDeg)
                Fail($"flexible joints: the {how} didn't swing and come back as a joint should ({Deg(hang):0.0}, {Deg(peak):0.0}, {Deg(back):0.0})");
        }
        Line($"flexible joints, welds: {string.Join("; ", results)}");
    }

    /// <summary>A motor hinge beyond a flexible joint still works; a wheel on a suspension beyond one rests on the ground without trouble; a shaft can't cross it and says why.</summary>
    private static void FlexBeyond()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (FxArm(w, q, "Ball Joint 2x2", out var jp, out var beam) is not { } c)
        {
            Fail("flexible joints: the arm doesn't build");
            return;
        }
        var mh = FitTurned(w, c, beam, "end+", "Motor Hinge", "base", _ => true);
        var panel = mh < 0 ? -1 : FitTurned(w, c, mh, "leaf", "Frame Block", "-x", _ => true);
        var lever = FitTurned(w, c, 0, "top00", "Lever", "-y", _ => true);
        if (panel < 0 || lever < 0 || !w.AddWire(c, lever, mh))
        {
            Fail("flexible joints: a motor hinge beyond the joint doesn't build");
            return;
        }
        var outer = c.JointOf(mh);
        c.Wake();
        FxStep(w, q, 120);
        var bend0 = FxBend(c, jp);
        var a0 = c.Joints[outer].Angle;
        c.Parts[lever].State = 1;
        c.Wake();
        FxStep(w, q, 180);
        var a1 = c.Joints[outer].Angle;
        Line($"flexible joints, beyond: a motor hinge on the arm's end goes {Deg(a0):0.0} to {Deg(a1):0.0} deg, the arm hanging {Deg(bend0):0.0} to {Deg(FxBend(c, jp)):0.0}; parent chain {c.Joints[outer].ParentSeg} of {c.JointCount}");
        if (!(a1 - a0 > 0.5f) || c.Joints[outer].ParentSeg != c.LeafSegOf(c.JointOf(jp)))
            Fail("flexible joints: a motor hinge beyond the flexible joint doesn't work");

        // A wheel on a suspension beyond the joint, on a free hull, pushed along: it stays with the arm and nothing goes wild.
        var fw = new ConstructWorld();
        var fq = new JointTestWorld();
        if (DriveBlueprint("Buggy") is not { } bp || DriveSpawn(fw, bp, 0, 0, 0, "the buggy") is not { } car)
            return;
        DriveSteps(fw, fq, 60);
        var frame = -1;
        var fj = -1;
        for (var i = 0; i < car.PartCount && fj < 0; i++)
            for (var s = 0; s < car.DefOf(i).Sockets.Length && fj < 0; s++)
                if (car.Parts[i].Seg == 0 && car.DefOf(i).Sockets[s].Kind == SocketKind.Struct && !car.SocketUsed(i, s)
                    && ConstructBuild.Place(car, i, s, ConstructParts.Need("Flex Joint 1x1"), 0, 0, out var g) == null)
                {
                    fj = ConstructBuild.Attach(fw, car, ConstructParts.Need("Flex Joint 1x1"), g);
                    frame = i;
                }
        var susp = fj < 0 ? -1 : FitTurned(fw, car, fj, "top", "Frame Block", "-y", _ => true);
        var wheels = car.WheelCount;
        car.Rebuild();
        if (frame < 0 || susp < 0)
        {
            Fail("flexible joints: nothing fits a flexible joint on the buggy");
            return;
        }
        var drove = 0f;
        var from = car.Pos;
        var ok = true;
        for (var t = 0; t < 180; t++)
        {
            car.Throttle = 1;
            car.Wake();
            fw.Step(fq, World.TickDt);
            ok &= float.IsFinite(car.Pos.X + car.Pos.Z);
        }
        drove = Vector3.Distance(from, car.Pos);
        Line($"flexible joints, on a buggy: a block on a flexible joint: {wheels} wheels still drive, rolled {drove:0.0} m");
        if (!ok || car.WheelCount != wheels)
            Fail("flexible joints: a piece on a flexible joint upset the buggy");
    }

    /// <summary>A player stands on the arm with both movers: stays on it, and their weight bends it further.</summary>
    private static void FlexRiders()
    {
        var saved = World.Mover;
        try
        {
            foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
            {
                World.Mover = mover;
                var w = new ConstructWorld();
                var q = World.Query;
                var ground = ConstructWorld.Ground(q, 0, 0);
                if (FxArm(w, q, "Flex Joint 1x1", out var jp, out var beam) is not { } c)
                {
                    Fail("flexible joints: the riders' arm doesn't build");
                    return;
                }
                var over = c.Parts[beam].Pos + new Vector3(0, 0.25f, 0);
                c.Wake();
                FxStep(w, q, 180);
                var alone = FxBend(c, jp);
                var s = new MoveState { Position = c.ToWorld(over) + new Vector3(0, 0.3f, 0), Grounded = false };
                var off = 0;
                var deepest = 0f;
                for (var t = 0; t < 240; t++)
                {
                    c.Wake();
                    w.Step(q, World.TickDt);
                    World.Simulate(ref s, new InputCmd { Seq = (uint)t + 1 });
                    w.BlockPlayer(ref s, World.PlayerHeight, server: true, who: 1);
                    deepest = MathF.Max(deepest, FxBend(c, jp));
                    if (t > 60 && !s.Grounded)
                        off++;
                }
                var height = s.Position.Y - ground.Y;
                Line($"flexible joints, rider ({mover.Mode}): the arm alone {Deg(alone):0.0} deg, with a player on it {Deg(FxBend(c, jp)):0.0} (most {Deg(deepest):0.0}); feet {height:0.00} m up, {off} ticks off it");
                if (off > 3 || height < 0.5f)
                    Fail($"flexible joints: a player didn't stay on the arm with the {mover.Mode} mover ({off} ticks off it, {height:0.00} m up)");
                if (!(deepest > alone + 0.2f * FxDeg))
                    Fail($"flexible joints: a player's weight didn't bend the arm with the {mover.Mode} mover");
            }
        }
        finally
        {
            World.Mover = saved;
        }
    }

    /// <summary>
    /// The chute rig: a pinned foundation with a bin and a parts shelf on it; a frame block; the joint on the block's top, a
    /// block on its leaf with a second bin and shelf on that, and a lever wired into the joint.
    /// </summary>
    private static Construct? FxChuteRig(ConstructWorld w, IWorldQuery q, string joint, out int jp, out int a, out int b, out int lever, float x = 0, float z = 0)
    {
        jp = a = b = lever = -1;
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, x, z), 0) is not { } c)
            return null;
        c.Pinned = true;
        a = FitTurned(w, c, 0, "top00", "Materials Bin", "bottom", _ => true);
        var block = FitTurned(w, c, 0, "top22", "Frame Block", "-y", _ => true);
        var d = ConstructParts.Need(joint);
        jp = block < 0 ? -1 : FitTurned(w, c, block, "+y", joint, BaseSocket(d), _ => true);
        var top = jp < 0 ? -1 : FitTurned(w, c, jp, d.LeafSocket, "Frame Block", "-y", _ => true);
        b = top < 0 ? -1 : FitTurned(w, c, top, "+y", "Materials Bin", "bottom", _ => true);
        lever = FitTurned(w, c, 0, "top30", "Lever", "-y", _ => true);
        if (a < 0 || b < 0 || lever < 0)
            return null;
        if (d.JointType != JointType.Flex)
            w.AddWire(c, lever, jp);
        c.Rebuild();
        return c;
    }

    private static string? FxChute(ConstructWorld w, Construct c, int from, int to)
    {
        var why = ConstructRoutes.Check(w, c, RouteType.Chute, from, 255, 255, 0, default, default, to, 255);
        if (why == null && ConstructRoutes.Add(w, c, RouteType.Chute, from, 255, 255, 0, default, default, to, 255) < 0)
            why = "no room";
        c.Rebuild();
        return why;
    }

    /// <summary>A chute across a moving hinge, piston, turntable and flexible joint keeps carrying bulk while the joint moves, and a parts shelf's parts; a flexible section's look line says how far it is.</summary>
    private static void FlexChuteHinge()
    {
        foreach (var joint in new[] { "Motor Hinge", "Piston 2x2 Medium", "Turntable", "Ball Joint 2x2" })
        {
            var w = new ConstructWorld();
            var q = new JointTestWorld();
            if (FxChuteRig(w, q, joint, out var jp, out var a, out var b, out var lever) is not { } c)
            {
                Fail($"flexible joints: the {joint} chute rig doesn't build");
                return;
            }
            var why = FxChute(w, c, a, b);
            var sections = c.FlexSectionCount;
            w.Deposit(c, a, 100, 1);
            var rest = c.FlexSections?[0].Rest ?? 0;
            c.Parts[lever].State = 1;
            if (c.JointOf(jp) is var j and >= 0 && c.Joints[j].Type == JointType.Flex)
                c.Joints[j].Vel = 2;
            c.Wake();
            var moved = 0f;
            var j0 = c.JointOf(jp);
            var angle0 = j0 >= 0 ? c.Joints[j0].Angle : 0;
            var most = 0f;
            for (var t = 0; t < 3 * World.TickRate + 2; t++)
            {
                c.Wake();
                w.Step(q, World.TickDt);
                moved = MathF.Max(moved, MathF.Abs(c.Joints[j0].Angle - angle0));
                if (sections > 0)
                    most = MathF.Max(most, ConstructRoutes.FlexStrain(c, 0, out _, out _));
            }
            var there = w.StoreOf(c, b)?.Amount ?? -1;
            var note = ConstructRoutes.FlexNote(c, 0);
            Line($"flexible joints, chute over the {joint}: {why ?? "routed"}, {sections} flexible section, {rest:0.00} m at rest, joint moved {moved:0.00}, strain at most {most:0.00}: {there} of 100 went over; \"{note}\"");
            if (why != null || sections != 1)
                Fail($"flexible joints: a chute over the {joint} wasn't made a flexible section ({why})");
            if (there < ConstructRoutes.ChuteLoad * 2)
                Fail($"flexible joints: the chute over the moving {joint} carried {there}");
            if (!(moved > 0.1f))
                Fail($"flexible joints: the {joint} under the chute didn't move ({moved:0.00})");
        }
        // Parts: two shelves over a hinge.
        var pw = new ConstructWorld();
        var pq = new JointTestWorld();
        if (ConstructBuild.PlaceNew(pw, ConstructParts.Need("Foundation"), Vector3.Zero, 0) is not { } s)
            return;
        s.Pinned = true;
        var s1 = FitTurned(pw, s, 0, "top00", "Parts Shelf", "bottom", _ => true);
        var blk = FitTurned(pw, s, 0, "top32", "Frame Block", "-y", _ => true);
        var hinge = blk < 0 ? -1 : FitTurned(pw, s, blk, "+y", "Motor Hinge", "base", _ => true);
        var top = hinge < 0 ? -1 : FitTurned(pw, s, hinge, "leaf", "Frame Block", "-x", _ => true);
        var s2 = top < 0 ? -1 : FitTurned(pw, s, top, "+y", "Parts Shelf", "bottom", _ => true);
        var lev = FitTurned(pw, s, 0, "top30", "Lever", "-y", _ => true);
        if (s1 < 0 || s2 < 0 || lev < 0 || !pw.AddWire(s, lev, hinge))
        {
            Fail($"flexible joints: the shelves over a hinge don't build ({s1}, {s2}, {lev})");
            return;
        }
        var chute = FxChute(pw, s, s1, s2);
        var lamp = ConstructParts.Need("Lamp");
        var putIn = pw.PutPart(s, s1, lamp);
        pw.PutPart(s, s1, lamp);
        s.Parts[lev].State = 1;
        s.Wake();
        for (var t = 0; t < 3 * World.TickRate + 2; t++)
        {
            s.Wake();
            pw.Step(pq, World.TickDt);
        }
        var over = pw.StoreOf(s, s2)?.ItemCount ?? -1;
        var left = pw.StoreOf(s, s1)?.ItemCount ?? -1;
        Line($"flexible joints, parts: {chute ?? "routed"} ({putIn ?? "two lamps on the shelf"}): {over} moved over a moving hinge, {left} left");
        if (chute != null || over != 2 || left != 0)
            Fail("flexible joints: a chute over a moving hinge didn't carry parts");
    }

    /// <summary>A piston past a flexible section's reach stops the chute with the reason and carries on as it comes back; a turntable turned past tearing tears it, with a hint, and the wrench's repair mends it.</summary>
    private static void FlexChuteLimits()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (FxChuteRig(w, q, "Piston", out var jp, out var a, out var b, out var lever) is not { } c)
        {
            Fail("flexible joints: the piston chute rig doesn't build");
            return;
        }
        var fail = FxChute(w, c, a, b);
        if (fail != null || c.FlexSectionCount != 1)
        {
            Fail($"flexible joints: the chute over the piston: {fail}");
            return;
        }
        var d = c.DefOf(jp);
        w.Deposit(c, a, 300, 1);
        var limit = ConstructRoutes.FlexMaxLength(c.FlexSections![0].Rest);
        c.Parts[lever].State = 1;
        var stoppedAt = -1f;
        var note = "";
        var kept = 0;
        for (var t = 0; t < 4 * World.TickRate; t++)
        {
            c.Wake();
            w.Step(q, World.TickDt);
            if (stoppedAt < 0 && ConstructRoutes.FlexStopped(c, 0) >= 0)
            {
                ConstructRoutes.FlexStrain(c, 0, out stoppedAt, out _);
                kept = w.StoreOf(c, b)?.Amount ?? 0;
            }
        }
        note = ConstructRoutes.FlexNote(c, 0) ?? "";
        var stuck = w.StoreOf(c, b)?.Amount ?? -1;
        ConstructRoutes.FlexStrain(c, 0, out var len, out _);
        var torn = ConstructRoutes.SegmentHealth(c, 0, c.FlexSections[0].Seg) <= 0;
        Line($"flexible joints, limit: rest {c.FlexSections[0].Rest:0.00} m, reach {limit:0.00} m, piston travel {d.JointMax:0.0} m: stopped at {stoppedAt:0.00} m with {kept} over; {stuck} over at {len:0.00} m, torn {torn}: \"{note}\"");
        var tornByPiston = torn;
        if (stoppedAt < 0)
            Fail($"flexible joints: the chute over the piston never stopped (the section reached {len:0.00} m of {limit:0.00})");
        else if (!tornByPiston && (!note.Contains("STOPPED") || stuck > kept + ConstructRoutes.ChuteLoad))
            Fail($"flexible joints: past its limit the chute carried on or didn't say why (\"{note}\", {kept} then {stuck})");
        // Back: it carries on (a section that tore waits for the wrench).
        c.Parts[lever].State = 0;
        for (var t = 0; t < 3 * World.TickRate; t++)
        {
            c.Wake();
            w.Step(q, World.TickDt);
        }
        if (tornByPiston)
            ConstructRoutes.Repair(w, c, 0);
        var then = w.StoreOf(c, b)?.Amount ?? -1;
        for (var t = 0; t < 3 * World.TickRate; t++)
        {
            c.Wake();
            w.Step(q, World.TickDt);
        }
        var after = w.StoreOf(c, b)?.Amount ?? -1;
        Line($"flexible joints, limit: the piston back in: {then} then {after} over");
        if (after <= then)
            Fail("flexible joints: the chute didn't carry on when the joint came back");

        // A turntable turned past 135 degrees stops it, past tearing it tears.
        var tw = new ConstructWorld();
        if (FxChuteRig(tw, q, "Turntable", out var tp, out var ta, out var tb, out var tl) is not { } tc || FxChute(tw, tc, ta, tb) is { } terr)
        {
            Fail("flexible joints: the turntable chute doesn't build");
            return;
        }
        tw.Deposit(tc, ta, 300, 1);
        tc.Parts[tl].State = 1;
        var stopBend = -1f;
        var tornAt = -1f;
        tw.Hints.Clear();
        var seg = tc.FlexSections![0].Seg;
        for (var t = 0; t < 5 * World.TickRate && tornAt < 0; t++)
        {
            tc.Wake();
            tw.Step(q, World.TickDt);
            ConstructRoutes.FlexStrain(tc, 0, out _, out var bend);
            if (stopBend < 0 && ConstructRoutes.FlexStopped(tc, 0) >= 0)
                stopBend = bend;
            if (tc.Dirty)
                tc.Rebuild();
            if (ConstructRoutes.SegmentHealth(tc, 0, seg) <= 0)
                tornAt = bend;
        }
        var hint = tw.Hints.Count > 0 ? tw.Hints[0].Text : "";
        tc.Parts[tl].State = 0;
        tc.Rebuild();
        var notePost = ConstructRoutes.FlexNote(tc, 0) ?? "";
        var fed = tc.Routes[0].FedEnd;
        var mended = ConstructRoutes.Repair(tw, tc, 0);
        tc.Rebuild();
        Line($"flexible joints, turntable: stopped at {stopBend:0} deg, tore at {tornAt:0} deg: \"{hint}\", look \"{notePost}\"; cut stops the flow {!fed}, mended {mended}");
        if (MathF.Abs(stopBend - ConstructRoutes.FlexBendDeg) > 6 || tornAt < ConstructRoutes.FlexBendDeg || !hint.Contains("tore") || fed || !mended || !tc.Routes[0].FedEnd)
            Fail("flexible joints: a turntable past a flexible section's bend didn't stop it, tear it and let it be mended");
        _ = tp;
    }

    /// <summary>A cargo hose between a "mothership" and a small vehicle: chutes at both ends, a load a second for bulk and parts; it pops out past its length unhurt.</summary>
    private static void FlexCargoHose()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), Vector3.Zero, 0) is not { } ship
            || ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), new Vector3(0, 0, 8), 0) is not { } dock)
            return;
        ship.Pinned = dock.Pinned = true;
        var bin = FitTurned(w, ship, 0, "top00", "Materials Bin", "bottom", _ => true);
        var reel = FitTurned(w, ship, 0, "top33", "Cargo Hose Reel", "-y", _ => true);
        var inlet = FitTurned(w, dock, 0, "top33", "Cargo Hose Inlet", "-y", _ => true);
        var into = FitTurned(w, dock, 0, "top00", "Materials Bin", "bottom", _ => true);
        if (bin < 0 || reel < 0 || inlet < 0 || into < 0)
        {
            Fail($"flexible joints: the cargo hose rig doesn't build ({bin}, {reel}, {inlet}, {into})");
            return;
        }
        var c1 = FxChute(w, ship, bin, reel);
        var c2 = FxChute(w, dock, inlet, into);
        var wrong = ConstructRoutes.Check(w, ship, RouteType.Chute, reel, 255, 255, 0, default, default, bin, 255);
        w.Deposit(ship, bin, 200, 1);
        var hand = ConstructWorld.HosePoint(ship, reel);
        w.Hands = id => id == 1 ? hand : null;
        var took = w.HoseUse(1, ship, reel, handsFree: true);
        hand = ConstructWorld.HosePoint(dock, inlet);
        w.Step(q, World.TickDt);
        var plugged = w.HoseUse(1, dock, inlet, handsFree: true);
        w.Hands = _ => null;
        var d0 = w.StoreOf(ship, bin)?.Amount ?? -1;
        FxStep(w, q, 3 * World.TickRate + 2);
        var moved = w.StoreOf(dock, into)?.Amount ?? -1;
        var left = w.StoreOf(ship, bin)?.Amount ?? -1;
        // The other way round isn't what it does; parts go too.
        var hp = dock.Parts[inlet].Health;
        ship.Pinned = false;
        ship.Rebuild();
        var length = ship.DefOf(reel).HoseLength;
        w.Hints.Clear();
        var popped = -1f;
        dock.Pinned = true;
        var ground0 = ship.Pos;
        for (var t = 0; t < 6 * World.TickRate && popped < 0; t++)
        {
            ship.Pos = ship.PrevPos = ground0 + new Vector3(0, 0, -t * 0.1f);
            w.Step(q, World.TickDt);
            if (w.HoseCount == 0)
                popped = Vector3.Distance(ConstructWorld.HosePoint(ship, reel), ConstructWorld.HosePoint(dock, inlet));
        }
        var hint = w.Hints.Count > 0 ? w.Hints[0].Text : "";
        Line($"flexible joints, cargo hose: chutes {c1 ?? "routed"} and {c2 ?? "routed"}; a chute out of a reel: \"{wrong}\"; took {took ?? "ok"}, plugged {plugged ?? "ok"}: {d0} in the bin, {moved} over in 3 s, {left} left; popped out at {popped:0.0} m (length {length:0}): \"{hint}\"");
        if (c1 != null || c2 != null || wrong == null || took != null || plugged != null)
            Fail("flexible joints: the cargo hose rig didn't route or plug as it should");
        if (moved != 3 * ConstructRoutes.ChuteLoad || left != 200 - moved)
            Fail($"flexible joints: the cargo hose moved {moved} in 3 s (a load a second is {3 * ConstructRoutes.ChuteLoad})");
        if (popped < length || popped > length + 0.5f || !hint.Contains("popped out") || dock.Parts[inlet].Health < hp)
            Fail("flexible joints: the cargo hose didn't pop out at its length, unhurt");
    }

    /// <summary>Nothing allocates per tick with a bending arm, a chute over it and a plugged cargo hose; the arm's tick stays small.</summary>
    private static void FlexBudget()
    {
        var w = new ConstructWorld();
        var q = new JointTestWorld();
        if (FxChuteRig(w, q, "Ball Joint 2x2", out var jp, out var a, out var b, out _) is not { } c || FxChute(w, c, a, b) != null)
        {
            Fail("flexible joints: the budget rig doesn't build");
            return;
        }
        w.Deposit(c, a, 300, 1);
        var j = c.JointOf(jp);
        void Run(int ticks)
        {
            for (var t = 0; t < ticks; t++)
            {
                if (t % 30 == 0)
                {
                    c.Wake();
                    c.Joints[j].Vel = 3;
                    c.Joints[j + 1].Vel = -3;
                }
                w.Step(q, World.TickDt);
            }
        }
        Run(60);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(120);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var clock = Stopwatch.StartNew();
        Run(240);
        var ms = clock.Elapsed.TotalMilliseconds / 240;
        Line($"flexible joints, budget: {bytes} bytes over 120 ticks, {ms:0.000} ms a tick");
        if (bytes > 0)
            Fail($"flexible joints: the tick allocates ({bytes} B over 120 ticks)");
        if (ms > 2)
            Fail($"flexible joints: a tick took {ms:0.00} ms");
    }

    /// <summary>
    /// "Flexible joints network": a real host, a client over the transport with lag and loss, and a late joiner on the Construct
    /// Yard. Each sees the flexible joint as parts, joints and angles the server has, the same chute strain, the bin filling,
    /// a chute torn past its limit and a cargo hose plugged by the client.
    /// </summary>
    private static void CheckFlexJointsNetwork()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"flexible joints network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26317;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("flexible joints network: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var lagged = server.Net.SimulateBadNetwork(50, 10, 3) & a.Net.SimulateBadNetwork(50, 10, 3);
        var w = server.Constructs;
        var spot = OpenSpot(w, sp.Position);
        var q = World.Query;
        if (FxChuteRig(w, q, "Ball Joint 2x2", out var jp, out var binA, out var binB, out _, spot.X, spot.Z) is not { } rig || FxChute(w, rig, binA, binB) is { } why1)
        {
            Fail("flexible joints network: the rig doesn't build");
            return;
        }
        var spot2 = spot + new Vector3(0, 0, 6);
        if (FxChuteRig(w, q, "Turntable", out _, out var ta, out var tb, out var tl, spot2.X, spot2.Z) is not { } spin || FxChute(w, spin, ta, tb) != null)
        {
            Fail("flexible joints network: the turntable rig doesn't build");
            return;
        }
        // A cargo hose between two pinned foundations.
        var spot3 = spot + new Vector3(6, 0, 0);
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, spot3.X, spot3.Z), 0) is not { } st
            || ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(q, spot3.X + 4, spot3.Z), 0) is not { } dock)
        {
            Fail("flexible joints network: the hose rig doesn't build");
            return;
        }
        st.Pinned = dock.Pinned = true;
        int bin = FitTurned(w, st, 0, "top00", "Materials Bin", "bottom", _ => true), reel = FitTurned(w, st, 0, "top33", "Cargo Hose Reel", "-y", _ => true);
        int inlet = FitTurned(w, dock, 0, "top00", "Cargo Hose Inlet", "-y", _ => true), got = FitTurned(w, dock, 0, "top33", "Materials Bin", "bottom", _ => true);
        var c1 = FxChute(w, st, bin, reel);
        var c2 = FxChute(w, dock, inlet, got);
        w.Deposit(rig, binA, 200, 1);
        w.Deposit(st, bin, 200, 1);
        Pump(server, [a], 0.6, ref now, clock);
        // The joint swings on the server (its spring takes it back); the client plugs the hose.
        var j = rig.JointOf(jp);
        for (var i = 0; i < 3; i++)
        {
            rig.Wake();
            rig.Joints[j].Vel = 2;
            rig.Joints[j + 1].Vel = -2;
            Pump(server, [a], 0.2, ref now, clock);
        }
        server.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        server.SandboxHands.Hand(a.LocalId).Held = ConstructParts.None;
        var reelAt = ConstructWorld.HosePoint(st, reel);
        sp.Teleport(ConstructWorld.Ground(q, reelAt.X - 1, reelAt.Z));
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxSend(CActKind.Use, body: st.Id, part: (byte)reel);
        Pump(server, [a], 0.5, ref now, clock);
        var inletAt = ConstructWorld.HosePoint(dock, inlet);
        sp.Teleport(ConstructWorld.Ground(q, inletAt.X - 1, inletAt.Z));
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxSend(CActKind.Use, body: dock.Id, part: (byte)inlet);
        Pump(server, [a], 0.6, ref now, clock);
        var plugged = w.HoseCount == 1 && w.Hoses[0].Plugged;
        // The turntable turns the second chute until it tears.
        spin.Parts[tl].State = 1;
        spin.Wake();
        var waited = 0;
        var seg = spin.FlexSections![0].Seg;
        while (ConstructRoutes.SegmentHealth(spin, 0, seg) > 0 && waited++ < 30)
        {
            spin.Wake();
            Pump(server, [a], 0.25, ref now, clock);
        }
        spin.Parts[tl].State = 0;
        rig.Joints[j].Vel = 3;
        rig.Joints[j + 1].Vel = 1;
        rig.Wake();
        Pump(server, [a], 1.2, ref now, clock);

        string Mirror(Game client)
        {
            var cw = client.Constructs;
            if (cw.Get(rig.Id) is not { } cr || cw.Get(spin.Id) is not { } cs || cw.Get(dock.Id) is not { } cd)
                return "bodies missing";
            if (cr.JointCount != rig.JointCount)
                return $"{cr.JointCount} joints (server {rig.JointCount})";
            var cj = cr.JointOf(jp);
            for (var k = 0; k < 3; k++)
            {
                ref var js = ref rig.Joints[j + k];
                ref var jc = ref cr.Joints[cj + k];
                if (jc.Type != JointType.Flex || jc.FlexSub != k || jc.Hinge != js.Hinge || jc.Axis != js.Axis || jc.ParentSeg != js.ParentSeg)
                    return $"joint {k} differs: {jc.Type}, sub {jc.FlexSub}, axis {jc.Axis}";
                if (MathF.Abs(jc.Angle - js.Angle) > 0.2f)
                    return $"joint {k}'s angle {jc.Angle:0.00} (server {js.Angle:0.00})";
            }
            if (cr.FlexSectionCount != rig.FlexSectionCount || cr.FlexSections == null)
                return "its flexible section is missing";
            if (MathF.Abs(ConstructRoutes.FlexStrain(cr, 0, out _, out _) - ConstructRoutes.FlexStrain(rig, 0, out _, out _)) > 0.25f)
                return "the strain differs";
            if (cs.Routes[0].FedEnd || ConstructRoutes.FlexNote(cs, 0)?.Contains("torn") != true)
                return $"the torn chute isn't torn here (\"{ConstructRoutes.FlexNote(cs, 0)}\")";
            if (cw.HoseCount != w.HoseCount || cw.Hoses[0].BodyIn != dock.Id)
                return "the hose differs";
            if (cw.StoreOf(cd, got)?.Amount is not { } there || Math.Abs(there - (w.StoreOf(dock, got)?.Amount ?? 0)) > 2 * ConstructRoutes.ChuteLoad)
                return "the hose's bin differs";
            return "";
        }
        var mirrorA = Mirror(a);
        var loaded = w.StoreOf(dock, got)?.Amount ?? -1;
        var over = w.StoreOf(rig, binB)?.Amount ?? -1;
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        var mirrorB = Mirror(b);
        Line($"flexible joints network: lag {lagged}; chutes {c1 ?? "routed"}, {c2 ?? "routed"}; the client plugged the hose {plugged}; torn after {waited} waits; {over} over the flexible joint, {loaded} down the hose; the client: {(mirrorA.Length == 0 ? "mirrors" : mirrorA)}; the late joiner: {(mirrorB.Length == 0 ? "mirrors" : mirrorB)}; notice \"{a.SandboxNotice}\"");
        if (!lagged || c1 != null || c2 != null || !plugged || loaded <= 0 || over <= 0)
            Fail("flexible joints network: the rig, the hose or the flow didn't work over the network");
        if (mirrorA.Length > 0 || mirrorB.Length > 0)
            Fail($"flexible joints network: a peer doesn't mirror the server (client: {mirrorA}; late joiner: {mirrorB})");
        yard.RestoreProps();
    }
}
