using System.Numerics;

namespace CallOfSlop;

/// <summary>A chute's flexible section (rebuilt with the links): segment <see cref="Seg"/> of link <see cref="Route"/> runs across a moving joint.</summary>
public struct CFlexSection
{
    public byte Route;
    public ushort Seg; // (as in ConstructRoutes.Point: from point Seg - 1 to point Seg)
    public float Rest; // its length with every joint at rest
}

public sealed partial class Construct
{
    public const int MaxFlexSections = 32;
    public CFlexSection[]? FlexSections; // (made the first time a chute crosses a joint)
    public int FlexSectionCount;
}

/// <summary>
/// Flexible chutes (docs/sandbox/CONSTRUCTS.md, "Flexible chutes"). A chute's segment whose two ends move apart, across a
/// hinge, a piston, a turntable or a flexible joint, is a flexible section, drawn as a bellows that follows its ends and
/// free of the duct's 45 degree steps. It has a limit (<see cref="FlexMaxLength"/>, <see cref="FlexBendDeg"/>): past it the
/// chute stops carrying, with the reason on its look line, and past <see cref="FlexTear"/> times its limit it tears (cut,
/// as if shot: the wrench mends it). Nothing new is sent: both peers work a section's strain out from the joints.
/// </summary>
public static partial class ConstructRoutes
{
    public const float FlexStretch = 3f; // a flexible section stretches to this many times its rest length...
    public const float FlexReach = 0.5f; // ...and at least this much past it
    public const float FlexSquash = 0.3f; // it folds up to this share of its rest length
    public const float FlexBendDeg = 135f; // its two ends turn this far apart
    public const float FlexTear = 1.25f; // past this many times its limit it tears

    public static float FlexMaxLength(float rest) => MathF.Max(rest * FlexStretch, rest + FlexReach);

    public static float FlexMinLength(float rest) => rest * FlexSquash;

    /// <summary>Is the stretch of path between hosts on segments <paramref name="a"/> and <paramref name="b"/> a flexible section of a link of <paramref name="type"/>?</summary>
    public static bool Flexes(Construct c, RouteType type, int a, int b) => type == RouteType.Chute && c.MoveSeg(a) != c.MoveSeg(b);

    /// <summary>Finds the chutes' flexible sections (from <see cref="Rebuild"/>; no allocation after a construct's first).</summary>
    private static void RebuildFlex(Construct c)
    {
        c.FlexSectionCount = 0;
        if (c.MovingJoints == 0)
            return;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Chute)
                continue;
            var a = Point(c, k, -1, out var sa);
            for (var i = 0; i <= r.Count; i++)
            {
                var b = Point(c, k, i, out var sb);
                if (Flexes(c, r.Type, sa, sb) && c.FlexSectionCount < Construct.MaxFlexSections)
                {
                    c.FlexSections ??= new CFlexSection[Construct.MaxFlexSections];
                    c.FlexSections[c.FlexSectionCount++] = new CFlexSection { Route = (byte)k, Seg = (ushort)i, Rest = Vector3.Distance(c.ToRest(sa, a), c.ToRest(sb, b)) };
                }
                a = b;
                sa = sb;
            }
        }
    }

    /// <summary>
    /// Flexible section <paramref name="i"/> as the joints stand: its length and how far its two ends have turned apart
    /// (degrees). Returns its strain: 1 at its limit, the largest of its stretch, its folding and its bend.
    /// </summary>
    public static float FlexStrain(Construct c, int i, out float length, out float bend)
    {
        ref var f = ref c.FlexSections![i];
        var a = Point(c, f.Route, f.Seg - 1, out var sa);
        var b = Point(c, f.Route, f.Seg, out var sb);
        c.SegPose(sa, out var qa, out var ta);
        c.SegPose(sb, out var qb, out var tb);
        length = Vector3.Distance(ta + Vector3.Transform(a, qa), tb + Vector3.Transform(b, qb));
        if (c.FreeformCount > 0)
        {
            // (a freeform weld's fixed turn is no bend: only what moved since rest counts)
            c.RestPose(sa, out var ra, out _);
            c.RestPose(sb, out var rb, out _);
            qa *= Quaternion.Conjugate(ra);
            qb *= Quaternion.Conjugate(rb);
        }
        var rel = Quaternion.Conjugate(qa) * qb;
        bend = 2 * MathF.Acos(MathF.Min(1, MathF.Abs(rel.W))) * 180f / MathF.PI;
        return MathF.Max(MathF.Max(length / MathF.Max(0.01f, FlexMaxLength(f.Rest)), FlexMinLength(f.Rest) / MathF.Max(0.01f, length)), bend / FlexBendDeg);
    }

    /// <summary>The flexible section a segment of a link is, or -1.</summary>
    public static int FlexSectionAt(Construct c, int route, int seg)
    {
        for (var i = 0; i < c.FlexSectionCount; i++)
            if (c.FlexSections![i].Route == route && c.FlexSections[i].Seg == seg)
                return i;
        return -1;
    }

    /// <summary>The flexible section past its limit on link <paramref name="route"/> or on the links it branches from, or -1. Nothing allocates.</summary>
    public static int FlexStopped(Construct c, int route)
    {
        for (var i = 0; i < c.FlexSectionCount; i++)
        {
            var on = false;
            for (int k = route, guard = 0; (uint)k < (uint)c.RouteCount && guard <= Construct.MaxRoutes && !on; guard++)
            {
                on = c.FlexSections![i].Route == k;
                k = c.Routes[k].Parent;
            }
            if (on && SegmentHealth(c, c.FlexSections![i].Route, c.FlexSections[i].Seg) > 0 && FlexStrain(c, i, out _, out _) > 1)
                return i;
        }
        return -1;
    }

    /// <summary>Server, each tick of an awake construct: a flexible section stretched past tearing tears.</summary>
    private static void StepFlex(ConstructWorld world, Construct c)
    {
        if (c.Asleep)
            return;
        for (var i = c.FlexSectionCount - 1; i >= 0; i--)
        {
            ref var f = ref c.FlexSections![i];
            if (SegmentHealth(c, f.Route, f.Seg) <= 0 || FlexStrain(c, i, out _, out _) <= FlexTear)
                continue;
            var at = WorldPoint(c, f.Route, f.Seg);
            if (Damage(world, c, f.Route, f.Seg, float.MaxValue))
                world.Hints.Add((at, "a flexible chute tore: its joint moved past what it reaches. Mend it with the attach tool"));
        }
    }

    /// <summary>What the look line adds for a chute with a flexible section: how far it is from its limit, and why it has stopped. Null for the rest.</summary>
    public static string? FlexNote(Construct c, int route)
    {
        var worst = -1;
        var most = 0f;
        for (var i = 0; i < c.FlexSectionCount; i++)
            if (c.FlexSections![i].Route == route)
            {
                var s = FlexStrain(c, i, out _, out _);
                if (worst < 0 || s > most)
                    (worst, most) = (i, s);
            }
        if (worst < 0)
            return null;
        ref var f = ref c.FlexSections![worst];
        if (SegmentHealth(c, f.Route, f.Seg) <= 0)
            return "flexible section torn: mend it with the attach tool";
        FlexStrain(c, worst, out var len, out var bend);
        var over = len > FlexMaxLength(f.Rest) ? "stretched too far" : len < FlexMinLength(f.Rest) ? "folded up too far" : bend > FlexBendDeg ? "bent too far" : null;
        var state = $"flexible section {len:0.0} of {FlexMaxLength(f.Rest):0.0} m, bent {bend:0} of {FlexBendDeg:0} deg";
        return over != null ? $"{state}: STOPPED, {over} (bring the joint back)" : state;
    }

    /// <summary>A number that changes when <see cref="FlexNote"/>'s text does (the look line's cache).</summary>
    public static long FlexKey(Construct c, int route)
    {
        var key = 0L;
        for (var i = 0; i < c.FlexSectionCount; i++)
            if (c.FlexSections![i].Route == route)
            {
                FlexStrain(c, i, out var len, out var bend);
                key = key * 31 + (long)(len * 10) * 1009 + (long)bend;
            }
        return key;
    }

    /// <summary>The way a flexible section leaves each end, in that end's segment frame (straight when at rest; the bellows curves from them).</summary>
    public static void FlexEnds(Construct c, int i, out Vector3 fromA, out Vector3 fromB)
    {
        ref var f = ref c.FlexSections![i];
        var a = Point(c, f.Route, f.Seg - 1, out var sa);
        var b = Point(c, f.Route, f.Seg, out var sb);
        var chord = c.ToRest(sb, b) - c.ToRest(sa, a);
        chord = chord.LengthSquared() > 1e-8f ? Vector3.Normalize(chord) : Vector3.UnitY;
        fromA = fromB = chord;
        if (c.FreeformCount == 0)
            return;
        c.RestPose(sa, out var ra, out _);
        c.RestPose(sb, out var rb, out _);
        fromA = Vector3.Transform(chord, Quaternion.Conjugate(ra));
        fromB = Vector3.Transform(chord, Quaternion.Conjugate(rb));
    }

    /// <summary>
    /// One move of a cargo hose: from a storage part whose chute comes into the reel, into one that a chute leaves the
    /// inlet for (<see cref="ChuteMove"/>), each way round in turn. True when something moved.
    /// </summary>
    public static bool HoseMove(ConstructWorld world, Construct rc, int reel, Construct ic, int inlet, ref byte cursor)
    {
        if (rc.Dirty)
            rc.Rebuild();
        if (ic.Dirty)
            ic.Rebuild();
        for (var a = 0; a < rc.RouteCount; a++)
        {
            var k = (a + cursor) % rc.RouteCount;
            ref var r = ref rc.Routes[k];
            if (r.Type != RouteType.Chute || r.To != reel || !r.FedEnd || FlexStopped(rc, k) >= 0)
                continue;
            for (var m = 0; m < ic.RouteCount; m++)
            {
                ref var o = ref ic.Routes[m];
                if (o.Type != RouteType.Chute || o.Source != inlet || !o.FedEnd || FlexStopped(ic, m) >= 0)
                    continue;
                if (ChuteMove(world, rc, r.Source, ic, o.To))
                {
                    cursor++;
                    return true;
                }
            }
        }
        return false;
    }
}
