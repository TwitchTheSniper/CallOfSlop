using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Parts welded to a walker leg's segments (docs/sandbox/GIANTS.md, phase 3). A leg part has a weld socket on each segment
/// (<c>seg0</c>, <c>seg1</c>..., added at load: <see cref="ConstructParts.WithLegSockets"/>) placed on a canonical rest pose:
/// the leg hanging straight down from its hip housing, its outer face +x. A plain part welded there (and plain parts welded
/// only to such parts) is a rider: in the rest frame it sits on that canonical segment, and it is drawn, hit and carried on
/// the segment as the leg is posed now. A rider adds its mass to the body and to the leg (which steps slower for it), takes
/// hits and damage, and never changes the rig. Its boxes are out of <see cref="SolidCount"/> (so out of the collider, the
/// decks, the fused units and the rays' part loop) and out of the merged mesh; the leg code draws and ray-tests them.
/// </summary>
public sealed partial class Construct
{
    public ushort[] RideOf = new ushort[FirstParts]; // a rider's leg part, plus one (0: not a rider)
    public byte[] RideSeg = new byte[FirstParts]; // ...and which of its segments it rides
    public int RiderCount;

    /// <summary>Is part <paramref name="i"/> a rider on a leg segment?</summary>
    public bool Rides(int i) => (uint)i < (uint)RideOf.Length && RideOf[i] != 0;

    /// <summary>
    /// The riders (at a rebuild, before the collider and the part tree): plain parts on a leg's segment sockets, then plain
    /// parts welded to them, less any that is also welded to something that doesn't ride the same segment.
    /// </summary>
    internal void BuildRiders()
    {
        if (RideOf.Length < Parts.Length)
        {
            RideOf = new ushort[Parts.Length];
            RideSeg = new byte[Parts.Length];
        }
        else
            Array.Clear(RideOf);
        RiderCount = 0;
        var any = false;
        for (var l = 0; l < LinkCount; l++)
        {
            ref var k = ref Links[l];
            any |= SeedRider(k.A, k.SA, k.B) | SeedRider(k.B, k.SB, k.A);
        }
        if (!any)
            return;
        // Out along welds to plain parts on the body frame.
        for (var pass = 0; pass < 256; pass++)
        {
            var grew = false;
            for (var l = 0; l < LinkCount; l++)
            {
                ref var k = ref Links[l];
                grew |= Spread(k.A, k.B) | Spread(k.B, k.A);
            }
            if (!grew)
                break;
        }
        // Less any rider welded to a part that isn't riding the same segment (its own seat on the leg aside).
        for (var pass = 0; pass < 64; pass++)
        {
            var dropped = false;
            for (var l = 0; l < LinkCount; l++)
            {
                ref var k = ref Links[l];
                dropped |= Foreign(k.A, k.SA, k.B, k.SB) | Foreign(k.B, k.SB, k.A, k.SA);
            }
            if (!dropped)
                break;
        }
        for (var i = 0; i < PartCount; i++)
            if (RideOf[i] != 0)
                RiderCount++;
    }

    private bool RiderKind(int i) => Parts[i].Seg == 0 && Parts[i].D.Leg == null && MergeOf[i] == 0 && PlainParts.Of(Parts[i].D);

    private bool SeedRider(int leg, byte socket, int other)
    {
        if (leg >= PartCount || other >= PartCount || Parts[leg].D.Leg is not { } d || Parts[leg].Seg != 0)
            return false;
        var s = socket - d.SegSocket;
        if (d.SegSocket < 0 || s < 0 || s >= d.Segments.Length || !RiderKind(other))
            return false;
        if (RideOf[other] != 0 && (RideOf[other] != leg + 1 || RideSeg[other] != s))
        {
            RideOf[other] = ushort.MaxValue; // (on two segments: it rides neither; dropped below)
            return false;
        }
        RideOf[other] = (ushort)(leg + 1);
        RideSeg[other] = (byte)s;
        return true;
    }

    private bool Spread(int from, int to)
    {
        if (from >= PartCount || to >= PartCount || RideOf[from] is 0 or ushort.MaxValue || RideOf[to] != 0 || !RiderKind(to))
            return false;
        RideOf[to] = RideOf[from];
        RideSeg[to] = RideSeg[from];
        return true;
    }

    private bool Foreign(int a, byte sa, int b, byte sb)
    {
        if (a >= PartCount || b >= PartCount || RideOf[a] == 0)
            return false;
        if (RideOf[a] == ushort.MaxValue)
        {
            RideOf[a] = 0;
            return true;
        }
        if (RideOf[a] == b + 1 && Parts[b].D.Leg is { } d && sb - d.SegSocket == RideSeg[a])
            return false; // (its seat on the leg)
        if (RideOf[b] == RideOf[a] && RideSeg[b] == RideSeg[a])
            return false;
        RideOf[a] = 0;
        return true;
    }

    /// <summary>kg riding leg part <paramref name="leg"/>'s segments.</summary>
    public float SegRiderMass(int leg)
    {
        if (RiderCount == 0)
            return 0;
        var m = 0f;
        for (var i = 0; i < PartCount; i++)
            if (RideOf[i] == leg + 1)
                m += Parts[i].D.Mass;
        return m;
    }
}

public static partial class ConstructPhysics
{
    private static readonly Vector3[] RidePts = new Vector3[LegDef.MaxSegments + 1];

    /// <summary>
    /// Segment <paramref name="s"/> of leg part <paramref name="part"/> on the canonical rest pose, in the body's rest frame:
    /// its middle and its frame (<see cref="BoneFrame"/>: length along z, outer face x).
    /// </summary>
    public static void RestSegment(Construct c, int part, int s, out Vector3 centre, out Quaternion q)
    {
        ref var p = ref c.Parts[part];
        var d = p.D.Leg!;
        centre = p.Pos + Rot24.Apply(p.Rot, d.RestCentre[s]);
        q = Rot24.Quat(p.Rot) * LegDef.RestFrame;
    }

    /// <summary>
    /// Where segment <paramref name="s"/> of leg <paramref name="k"/> carries what rides it, for a body at <paramref name="pos"/>,
    /// <paramref name="rot"/>: a rest-frame point x is at <c>t + q x</c> in the world.
    /// </summary>
    public static void RideFrame(Construct c, int k, int s, Vector3 pos, Quaternion rot, out Quaternion q, out Vector3 t)
    {
        var part = c.Legs[k].Part;
        var n = LegPose(c, k, pos, rot, RidePts, out var outward);
        s = Math.Min(s, n - 1);
        var bq = BoneFrame(RidePts[s], RidePts[s + 1], outward);
        var bc = (RidePts[s] + RidePts[s + 1]) * 0.5f;
        RestSegment(c, part, s, out var rc, out var rq);
        q = Quaternion.Normalize(bq * Quaternion.Conjugate(rq));
        t = bc - Vector3.Transform(rc, q);
    }

    /// <summary>The leg index of leg part <paramref name="part"/> (-1: none).</summary>
    public static int LegIndex(Construct c, int part)
    {
        for (var k = 0; k < c.LegCount; k++)
            if (c.Legs[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>
    /// The world frame a rest-frame point on part <paramref name="part"/> (at its socket <paramref name="socket"/>, -1: any)
    /// is drawn in, when that is a leg segment's: a rider's own segment, or the segment a leg's weld socket is on. False for
    /// anything else (use the part's segment frame).
    /// </summary>
    public static bool RideFrameOf(Construct c, int part, int socket, Vector3 pos, Quaternion rot, out Quaternion q, out Vector3 t)
    {
        q = Quaternion.Identity;
        t = Vector3.Zero;
        if ((uint)part >= (uint)c.PartCount || c.LegCount == 0)
            return false;
        int leg, s;
        if (c.Rides(part))
        {
            leg = c.RideOf[part] - 1;
            s = c.RideSeg[part];
        }
        else if (c.Parts[part].D.Leg is { SegSocket: >= 0 } d && socket >= d.SegSocket && socket < d.SegSocket + d.Segments.Length)
        {
            leg = part;
            s = socket - d.SegSocket;
        }
        else
            return false;
        var k = LegIndex(c, leg);
        if (k < 0)
            return false;
        RideFrame(c, k, s, pos, rot, out q, out t);
        return true;
    }

    /// <summary>
    /// A world point on leg part <paramref name="part"/> as drawn, taken to the rest frame of the segment nearest it (the
    /// welder aims at the drawn leg: its weld sockets are on the canonical pose). False when the part isn't a posed leg.
    /// </summary>
    public static bool LegPointToRest(Construct c, int part, Vector3 world, out Vector3 rest)
    {
        rest = world;
        var k = LegIndex(c, part);
        if (k < 0)
            return false;
        var n = LegPose(c, k, c.Pos, c.Rot, RidePts, out _);
        var best = 0;
        var bd = float.MaxValue;
        for (var s = 0; s < n; s++)
        {
            var a = RidePts[s];
            var ab = RidePts[s + 1] - a;
            var f = Math.Clamp(Vector3.Dot(world - a, ab) / MathF.Max(1e-6f, ab.LengthSquared()), 0, 1);
            var dd = Vector3.DistanceSquared(world, a + ab * f);
            if (dd < bd)
            {
                bd = dd;
                best = s;
            }
        }
        RideFrame(c, k, best, c.Pos, c.Rot, out var q, out var t);
        rest = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        return true;
    }

    /// <summary>A world point on a walker leg part or a part riding one, as drawn, in the rest frame (false: neither).</summary>
    public static bool RestOfDrawn(Construct c, int part, Vector3 world, out Vector3 rest)
    {
        rest = world;
        if ((uint)part >= (uint)c.PartCount || c.LegCount == 0)
            return false;
        if (c.Parts[part].D.Leg != null)
        {
            c.PartBounds(part, out var mn, out var mx);
            var local = c.ToLocal(world);
            if (Vector3.Clamp(local, mn - new Vector3(0.05f), mx + new Vector3(0.05f)) == local)
                return false; // (its hip housing: a part like any other)
            return LegPointToRest(c, part, world, out rest);
        }
        if (!RideFrameOf(c, part, -1, c.Pos, c.Rot, out var q, out var t))
            return false;
        rest = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        return true;
    }

    /// <summary>The riders of leg <paramref name="k"/> against a ray (rest-frame boxes carried by their segments); shortens <paramref name="best"/>.</summary>
    private static int RiderRay(Construct c, int k, Vector3 origin, Vector3 dir, ref float best, out Vector3 normal)
    {
        normal = -dir;
        var hit = -1;
        var leg = c.Legs[k].Part + 1;
        var segs = c.Parts[c.Legs[k].Part].D.Leg!.Segments.Length;
        for (var s = 0; s < segs; s++)
        {
            var framed = false;
            Quaternion q = default;
            Vector3 lo = default, ld = default;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (c.RideOf[i] != leg || c.RideSeg[i] != s)
                    continue;
                if (!framed)
                {
                    RideFrame(c, k, s, c.Pos, c.Rot, out q, out var t);
                    var iq = Quaternion.Conjugate(q);
                    lo = Vector3.Transform(origin - t, iq);
                    ld = Vector3.Transform(dir, iq);
                    framed = true;
                }
                var boxes = c.Parts[i].D.SolidBoxes;
                for (var b = 0; b < boxes; b++)
                {
                    c.BoxOf(i, b, out var centre, out var half);
                    if (Geometry.RayAabb(centre - half, centre + half, lo, ld, best) is { } d && d < best)
                    {
                        best = d;
                        hit = i;
                        normal = Vector3.Transform(Geometry.BoxEntryNormal(centre - half, centre + half, lo, ld, d), q);
                    }
                }
            }
        }
        return hit;
    }
}
