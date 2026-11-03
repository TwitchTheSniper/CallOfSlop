using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Flexible joints (docs/sandbox/CONSTRUCTS.md, "Flexible joints"): a small passive part joining two pieces with free play.
/// What hangs on its leaf bends any way up to a cone, springs back with a stiffness and a damping ratio, and on a ball
/// joint also twists a little. There is no ball in the solve: its ball is two or three of the ordinary one-number joints
/// in a row at one pivot (<see cref="JointType.Flex"/>, a swing about each axis square to the leaf, then a twist about the
/// leaf; the segments between them carry no parts), all naming the same part. So everything that walks a joint chain
/// (poses, the contact solve, couplings, recoil, riders, drawing, the wire) needs no third kind of joint, the pieces can
/// never drift apart, and a bent joint costs what two hinges cost. It also costs two or three of a construct's joints
/// (<see cref="ConstructPartDef.JointSlots"/>). <see cref="Construct.JointOf"/> finds the first of them and
/// <see cref="Construct.LeafSegOf"/> the segment the leaf's parts are on. The swings keep inside a circle
/// (swing a squared + swing b squared within the cone squared), not a square.
/// </summary>
public sealed partial class Construct
{
    /// <summary>The segment what hangs on joint <paramref name="j"/>'s leaf is on: its own, or a flexible joint's last.</summary>
    public int LeafSegOf(int j)
    {
        ref var jt = ref Joints[j];
        if (jt.Type != JointType.Flex)
            return j + 1;
        var s = j;
        while (s + 1 < JointCount && Joints[s + 1].Type == JointType.Flex && Joints[s + 1].Hinge == jt.Hinge)
            s++;
        return s + 1;
    }

    /// <summary>A flexible joint's settings from its part (a client's copy too), and which of its ball this joint is.</summary>
    private void RebuildFlex(int j)
    {
        ref var jt = ref Joints[j];
        jt.FlexSub = j > 0 && Joints[j - 1].Type == JointType.Flex && Joints[j - 1].Hinge == jt.Hinge ? (byte)(Joints[j - 1].FlexSub + 1) : (byte)0;
        if (jt.Hinge >= PartCount || Parts[jt.Hinge].D is not { JointType: JointType.Flex } d)
            return;
        ref var p = ref Parts[jt.Hinge];
        const float deg = MathF.PI / 180f;
        var cone = Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Max, d.JointMax / deg) * deg, 2 * deg, d.JointMax);
        var limit = jt.FlexSub == 2 ? d.FlexTwist : cone;
        jt.Min = -limit;
        jt.Max = limit;
        jt.Angle = Math.Clamp(jt.Angle, -limit, limit);
        jt.PrevAngle = Math.Clamp(jt.PrevAngle, -limit, limit);
        jt.Strength = d.MotorStrength;
        jt.Stiffness = MathF.Max(0.1f, ConstructConfig.Get(p, d, CfgKey.Stiffness, d.FlexStiffness * deg)) / deg;
        jt.FlexDamp = MathF.Max(0, ConstructConfig.Get(p, d, CfgKey.Damping, d.FlexDamping));
    }

    /// <summary>How far flexible joint part <paramref name="part"/> is bent now (radians; 0: it isn't one, or has nothing on it), and its limit.</summary>
    public float FlexBend(int part, out float cone)
    {
        cone = 0;
        var j = JointOf(part);
        if (j < 0 || Joints[j].Type != JointType.Flex || j + 1 >= JointCount)
            return 0;
        cone = Joints[j].Max;
        return MathF.Sqrt(Joints[j].Angle * Joints[j].Angle + Joints[j + 1].Angle * Joints[j + 1].Angle);
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>A flexible joint on its part: its swings and its twist, each hanging on the last at one pivot (returns the leaf's segment, or 0 when full).</summary>
    private byte AddFlexJoint(Construct c, int hinge)
    {
        ref var h = ref c.Parts[hinge];
        var d = h.D;
        var slots = d.JointSlots;
        if (c.JointCount + slots > Construct.MaxJoints || TotalJoints + slots > JointCap)
            return 0;
        var along = d.JointAxis >> 1; // (dir ids: axis * 2, plus 1 for the negative way)
        var parent = h.Seg;
        for (var k = 0; k < slots; k++)
        {
            var dir = k == 2 ? d.JointAxis : (byte)((along + 1 + k) % 3 * 2);
            c.Joints[c.JointCount] = new CJoint
            {
                Hinge = (byte)hinge,
                ParentSeg = parent,
                Pivot = h.Pos + Rot24.Apply(h.Rot, d.JointPivot),
                Axis = ConstructParts.DirVector(Rot24.ApplyDir(h.Rot, dir)),
                Min = k == 2 ? -d.FlexTwist : d.JointMin,
                Max = k == 2 ? d.FlexTwist : d.JointMax,
                Type = JointType.Flex,
                Strength = d.MotorStrength,
                Stiffness = d.FlexStiffness,
                FlexDamp = d.FlexDamping,
                FlexSub = (byte)k,
            };
            c.JointCount++;
            parent = (byte)c.JointCount;
        }
        return parent;
    }

    /// <summary>A shove on what hangs on a flexible joint: each of its swings and its twist picks up its share (<see cref="PushJoint"/>).</summary>
    private static void PushFlex(Construct c, int seg, Vector3 at, Vector3 push)
    {
        int hinge = c.Joints[seg - 1].Hinge;
        for (var guard = 0; seg > 0 && guard < 3 && c.Joints[seg - 1].Type == JointType.Flex && c.Joints[seg - 1].Hinge == hinge; guard++)
        {
            ref var j = ref c.Joints[seg - 1];
            c.WorldFrame(j.ParentSeg, out var q, out var t);
            var axis = Vector3.Transform(j.Axis, q);
            var r = at - (t + Vector3.Transform(j.Pivot, q));
            var torque = Vector3.Dot(Vector3.Cross(r, push), axis);
            var lever = MathF.Max(0.3f, (r - axis * Vector3.Dot(r, axis)).Length());
            if (!j.Locked)
                j.Vel = Math.Clamp(j.Vel + torque / (lever * lever) * 0.15f, -4f, 4f);
            seg = j.ParentSeg;
        }
        c.Wake();
    }
}

public static partial class ConstructPhysics
{
    /// <summary>
    /// A flexible joint's spring before the solve: an implicit (soft) row, so a stiff joint under a light piece can't blow up
    /// however it is set. The stiffness is the part's; the damping is a ratio of what critically damps this joint's load this
    /// tick, so it settles the same with a rock or a riding player on it. Its stops are the ordinary limit rows, the two
    /// swings' reaching a circle round rest: each swing may go as far as the other leaves it.
    /// </summary>
    private static void FlexPrep(in CJoint jt, int slot, float inertia, float dt)
    {
        var k = jt.Stiffness;
        var d = 2 * jt.FlexDamp * MathF.Sqrt(k * inertia);
        var soft = dt * (d + dt * k);
        _jGamma[slot] = soft > 1e-9f ? 1f / soft : 0;
        _jBias[slot] = jt.Angle * dt * k * _jGamma[slot];
    }

    /// <summary>How far the swing <paramref name="j"/> may reach this tick: the cone less what its other swing uses (a twist: its own limit).</summary>
    private static float FlexReach(Construct c, in CJoint jt, int j)
    {
        if (jt.FlexSub > 1)
            return jt.Max;
        var other = jt.FlexSub == 0 ? c.Joints[j + 1].Angle : c.Joints[j - 1].Angle;
        return MathF.Sqrt(MathF.Max(0, jt.Max * jt.Max - other * other));
    }

    /// <summary>After a joint of a flexible joint moved: what it held (its spring and stops) for the Load bar, and its swings kept inside the circle.</summary>
    private static void FlexAfter(Construct c, int j, int slot, float dt)
    {
        ref var jt = ref c.Joints[j];
        jt.Force += (MathF.Abs(_jP[slot] + _jLo[slot] + _jHi[slot]) / dt - jt.Force) * ForceSmooth;
        if (jt.FlexSub == 0)
            return;
        var head = j - jt.FlexSub;
        ref var h = ref c.Joints[head];
        if (jt.FlexSub == 1)
        {
            h.Over = MathF.Sqrt(h.Force * h.Force + jt.Force * jt.Force) / MathF.Max(1, h.Strength);
            var a2 = h.Angle * h.Angle + jt.Angle * jt.Angle;
            if (a2 > h.Max * h.Max && a2 > 0)
            {
                // Past the circle: back onto it along the line to rest, and no more speed outwards.
                var r = MathF.Sqrt(a2);
                var f = h.Max / r;
                var (ux, uy) = (h.Angle / r, jt.Angle / r);
                h.Angle *= f;
                jt.Angle *= f;
                var out_ = h.Vel * ux + jt.Vel * uy;
                if (out_ > 0)
                {
                    h.Vel -= out_ * ux;
                    jt.Vel -= out_ * uy;
                }
            }
        }
        else
            h.Over = MathF.Max(h.Over, jt.Force / MathF.Max(1, h.Strength));
        if (jt.FlexSub == 1 && h.Over > 1 && h.Hinge < c.PartCount && c.Parts[h.Hinge].D.OverloadDamage > 0)
            h.Strain += c.Parts[h.Hinge].D.OverloadDamage * (h.Over - 1) * dt;
    }
}
