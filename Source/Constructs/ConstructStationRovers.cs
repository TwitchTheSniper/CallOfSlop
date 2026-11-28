using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One arm rover class (the rover docks, Sean 2026-11-24, the Building Station Lab's approved design): its arm's reach and lift, its speed
/// (driving and the arm's hand), how fast it turns on the spot, how far its mast telescopes, its body's half length and half width, its
/// shoulder's height over the deck with the mast down, and the dock pad it parks on (2 m blocks: small 1 x 2, medium 2 x 3, large 4 x 6).
/// </summary>
public readonly record struct RoverClass(string Name, float Reach, float Kg, float Speed, float Turn, float Mast, float HalfLen, float HalfWid,
    float Shoulder, float Radius, float PadLen, float PadWid, int Wheels);

/// <summary>
/// An arm rover's trip (ConstructStationRovers.cs), the same on every peer: its body's keys (time, x, z, heading, in the bay's frame: x
/// across, z along, from the middle of its floor) and its hook's keys (time, offset from the body in the bay's axes; the hook is the
/// gripper's jaws, the wrist a metre over them). The body turns on the spot and then drives straight between keys; the hook swings round
/// the mast between its keys (in angle, radius and height, never through it). Times in seconds from the trip's start.
/// </summary>
public sealed class RoverPlan
{
    public const int MaxBase = 64, MaxHook = 14; // (protocol 486: 40 keys, for drives round what is fitted; 490: 64, for curves through corners, Smooth's bits)
    public int NB, NH;
    public ulong Smooth; // bit k: the body drives on through key k without a stop (a curve's point: Sean 2026-11-28, curve through corners)
    public readonly float[] Bt = new float[MaxBase], Bx = new float[MaxBase], Bz = new float[MaxBase], Bh = new float[MaxBase];
    public readonly float[] Ht = new float[MaxHook];
    public readonly Vector3[] Ho = new Vector3[MaxHook];
    public float Total, GripT, FitT, Up; // the trip's length; when the jaws close on the piece and when it goes on (0 for a trip home); the carry height
    public bool Home; // a trip home to its dock (no piece)
    public Quaternion Turn = Quaternion.Identity; // the piece's turn where it goes, in the bay's frame (the wrist turns it on the way)

    public void Clear()
    {
        NB = NH = 0;
        Smooth = 0;
        Total = GripT = FitT = Up = 0;
        Home = false;
        Turn = Quaternion.Identity;
    }

    public void CopyFrom(RoverPlan o)
    {
        (NB, NH, Total, GripT, FitT, Up, Home, Turn, Smooth) = (o.NB, o.NH, o.Total, o.GripT, o.FitT, o.Up, o.Home, o.Turn, o.Smooth);
        Array.Copy(o.Bt, Bt, NB);
        Array.Copy(o.Bx, Bx, NB);
        Array.Copy(o.Bz, Bz, NB);
        Array.Copy(o.Bh, Bh, NB);
        Array.Copy(o.Ht, Ht, NH);
        Array.Copy(o.Ho, Ho, NH);
    }

    public bool AddBase(float t, float x, float z, float h, bool smooth = false)
    {
        if (NB > 0 && t < Bt[NB - 1])
            t = Bt[NB - 1];
        var k = NB >= MaxBase ? NB - 1 : NB; // (full: the last key moves, so the trip still ends where it goes)
        (Bt[k], Bx[k], Bz[k], Bh[k]) = (t, x, z, h);
        Smooth = smooth ? Smooth | 1UL << k : Smooth & ~(1UL << k);
        if (k < NB)
            return false;
        NB++;
        return true;
    }

    private bool On(int k) => (Smooth >> k & 1) != 0;

    public void AddHook(float t, Vector3 off)
    {
        if (NH > 0 && t < Ht[NH - 1])
            t = Ht[NH - 1];
        if (NH >= MaxHook)
        {
            (Ht[NH - 1], Ho[NH - 1]) = (t, off);
            return;
        }
        (Ht[NH], Ho[NH]) = (t, off);
        NH++;
    }

    /// <summary>The body at <paramref name="t"/> s into the trip (before the start: the first key; past the end: the last).</summary>
    public void BaseAt(float t, out float x, out float z, out float h)
    {
        if (NB == 0)
        {
            x = z = h = 0;
            return;
        }
        if (t <= Bt[0] || NB == 1)
        {
            (x, z, h) = (Bx[0], Bz[0], Bh[0]);
            return;
        }
        if (t >= Bt[NB - 1])
        {
            (x, z, h) = (Bx[NB - 1], Bz[NB - 1], Bh[NB - 1]);
            return;
        }
        var k = 1;
        while (k < NB - 1 && Bt[k] < t)
            k++;
        // A curve (keys it drives on through): eased once over the whole run, by distance along it, so it keeps its speed round the
        // corners and stops only at the run's ends.
        int s = k - 1, e = k;
        while (s > 0 && On(s))
            s--;
        while (e < NB - 1 && On(e))
            e++;
        if (s < k - 1 || e > k)
        {
            var len = 0f;
            for (var q = s + 1; q <= e; q++)
                len += MathF.Sqrt((Bx[q] - Bx[q - 1]) * (Bx[q] - Bx[q - 1]) + (Bz[q] - Bz[q - 1]) * (Bz[q] - Bz[q - 1]));
            var span = Bt[e] - Bt[s];
            if (len > 1e-3f && span > 1e-4f)
            {
                var go = StationRovers.Ease((t - Bt[s]) / span) * len;
                for (var q = s + 1; q <= e; q++)
                {
                    var seg = MathF.Sqrt((Bx[q] - Bx[q - 1]) * (Bx[q] - Bx[q - 1]) + (Bz[q] - Bz[q - 1]) * (Bz[q] - Bz[q - 1]));
                    if (go > seg && q < e)
                    {
                        go -= seg;
                        continue;
                    }
                    var f = seg > 1e-4f ? Math.Clamp(go / seg, 0f, 1f) : 1f;
                    x = Bx[q - 1] + (Bx[q] - Bx[q - 1]) * f;
                    z = Bz[q - 1] + (Bz[q] - Bz[q - 1]) * f;
                    h = Bh[q - 1] + StationRovers.Wrap(Bh[q] - Bh[q - 1]) * f;
                    return;
                }
            }
        }
        var dt = Bt[k] - Bt[k - 1];
        var u = dt > 1e-4f ? (t - Bt[k - 1]) / dt : 1f;
        u = StationRovers.Ease(u);
        x = Bx[k - 1] + (Bx[k] - Bx[k - 1]) * u;
        z = Bz[k - 1] + (Bz[k] - Bz[k - 1]) * u;
        h = Bh[k - 1] + StationRovers.Wrap(Bh[k] - Bh[k - 1]) * u;
    }

    /// <summary>
    /// A carry's times from its hook keys (the same on every peer, since the keys are on the wire): when the piece is back up off its pad
    /// (<paramref name="lift"/>), when it is over its place (<paramref name="over"/>) and when it is down in its place for the weld
    /// (<paramref name="place"/>; the weld runs from there to <see cref="FitT"/>). All <see cref="GripT"/> for a trip with no piece.
    /// </summary>
    public void CarryTimes(out float lift, out float over, out float place)
    {
        lift = over = place = GripT;
        if (Home || FitT <= GripT || NH < 3)
            return;
        const float eps = 0.02f; // (the wire rounds times to a tick)
        var f = NH - 1;
        while (f > 0 && Ht[f] > FitT + eps)
            f--;
        if (f < 2)
            return;
        place = Math.Clamp(Ht[f - 1], GripT, FitT);
        over = Math.Clamp(Ht[f - 2], GripT, place);
        for (var k = 0; k < f; k++)
            if (Ht[k] > GripT + eps)
            {
                lift = Math.Min(Ht[k], over);
                break;
            }
    }

    /// <summary>
    /// How far into its driving pose the rover is drawn at <paramref name="t"/> (0 none, 1 tucked): over the keys where its hook stands
    /// tucked by the mast (<see cref="StationRovers.StowOf"/> at its first heading), eased in from the key before and out to the key after,
    /// so the pose never jumps. A carry holds a piece out of it; a trip home stays tucked to its pad.
    /// </summary>
    public float Tuck(float t, in RoverClass rc)
    {
        if (NH < 2 || NB == 0)
            return 0;
        var stow = StationRovers.StowOf(rc, Bh[0]);
        int k0 = -1, k1 = -1;
        for (var k = 1; k < NH; k++)
            if (Vector3.DistanceSquared(Ho[k], stow) < 1e-4f)
            {
                if (k0 < 0)
                    k0 = k;
                k1 = k;
            }
            else if (k0 >= 0)
                break;
        if (k0 < 0 || t <= Ht[k0 - 1])
            return 0;
        if (t < Ht[k0])
            return StationRovers.Ease((t - Ht[k0 - 1]) / MathF.Max(1e-3f, Ht[k0] - Ht[k0 - 1]));
        if (t <= Ht[k1] || k1 == NH - 1)
            return 1;
        return t >= Ht[k1 + 1] ? 0 : 1f - StationRovers.Ease((t - Ht[k1]) / MathF.Max(1e-3f, Ht[k1 + 1] - Ht[k1]));
    }

    /// <summary>The hook's offset from the body at <paramref name="t"/>: round the mast in angle, radius and height between its keys.</summary>
    public Vector3 HookAt(float t)
    {
        if (NH == 0)
            return new Vector3(0, 3, 2);
        if (t <= Ht[0] || NH == 1)
            return Ho[0];
        if (t >= Ht[NH - 1])
            return Ho[NH - 1];
        var k = 1;
        while (k < NH - 1 && Ht[k] < t)
            k++;
        var dt = Ht[k] - Ht[k - 1];
        var u = StationRovers.Ease(dt > 1e-4f ? (t - Ht[k - 1]) / dt : 1f);
        return StationRovers.Swing(Ho[k - 1], Ho[k], u);
    }
}

/// <summary>A dock pad of a Building Station: its part, its rover's class, the pad's middle and the way its mouth opens (into the bay), in the bay's frame.</summary>
public struct StationDock
{
    public ushort Part;
    public byte Cls;
    public Vector2 Mid, Dir;
}

public sealed partial class Construct
{
    // Every Dock Expansion part on the body (ConstructStationRovers.cs), again at a change of its parts.
    internal int StationDockStamp = -1, StationDockCount = -1;
    internal ushort[] StationDockParts = [];
    internal int StationDockN;
    // Each Building Station's clear height over its bay (ConstructStation.ClearHeight), by part, again at a change of its parts.
    internal int BayClearStamp = int.MinValue, BayClearParts = -1;
    internal Dictionary<int, float>? BayClear;
}

/// <summary>
/// The rover docks (Sean 2026-11-24, "Okay, I like it. Build it."; spec /mnt/project-files/building-station-lab/BUILD-SPEC.md, the Building
/// Station Lab, docs/sandbox/constructs/production.md "Building station"). A station's fitting arms are arm rovers that live in docks: the Core
/// is a dock pad for one small rover, each Dock Expansion welded mouth first to the bay's edge holds one more of its class. A rover drives
/// out of its dock, never jumps: it turns on the spot to face the way, then drives straight, round the parts already fitted
/// (RoverFloor: over them only when there is no room), squares up with its short end to the work, takes the piece off a pick pad from beside it (its body stays on the bay), swings it
/// round its mast and sets it on. It stays put when its spot reaches both the pick and the place, makes one stop that reaches both when it
/// can, and drives twice only when it must. A trip that meets another rover tries a spot beside along the lane, then a later start (up to
/// 300 s); an idle rover in the way is sent home. Idle with no ready step of its own, or a while, it drives home, folding as it goes. Pieces go to their class (8 t and 3 m to small,
/// 52 t and 10 m to medium, the rest to large); another class takes one only when it would have it on 90 s a class sooner. Server plans,
/// every peer draws the same trip from the wire (StationRender.cs). Nothing here allocates in a steady state.
/// </summary>
public static class StationRovers
{
    public static readonly RoverClass[] Classes =
    [
        default,
        new("Small rover", 9f, 8000f, 5f, 2.4f, 5f, 1.25f, 0.85f, 1.6f, 1.05f, 4f, 2f, 4),
        new("Medium rover", 15f, 52000f, 3f, 1.4f, 9f, 1.9f, 1.3f, 2.0f, 1.6f, 6f, 4f, 6),
        new("Large rover", 26f, 120000f, 1.5f, 0.6f, 13f, 5.0f, 3.25f, 3.6f, 4.0f, 12f, 8f, 8),
    ];

    public const float HandSpeed = 2f; // the hand swings round the mast this many times its rover's drive speed (Sean 2026-11-28)
    public const float VSpeed = 1.5f; // m/s the hook rises and falls
    public const float DropSpeed = 4f; // m/s the hook goes down onto a piece or its place and back up (Sean 2026-11-27: the drive is the time, the drop in is quick)
    public const float DropSeconds = 0.5f; // s a rover holds a piece on its place before it lets go (the fit; a crane's weld time is the station's own)
    public const float GripSeconds = 0.4f, ReleaseSeconds = 0.3f;
    public const float ClassPenalty = 5f; // s: a piece taken by an idle rover of another class costs this a class (a near tie's breaker: Sean 2026-11-28, any idle rover that lifts it goes; the lab's rule was 90 s, and a step waited for its busy class)
    public const float MaxWait = 300f; // s a trip waits for its way to clear before it goes anyway
    public const float HomeAfter = 15f; // s idle in the bay before a rover drives home (at once when no ready step is its: RoverJob JobSoon)
    public const float PickClear = 0.6f; // m between a rover's body and a pad's edge

    /// <summary>The class a piece goes to: 1 small (8 t, 3 m), 2 medium (52 t, 10 m), 3 large.</summary>
    public static int Want(ConstructPartDef d)
    {
        var s = d.BoundsMax - d.BoundsMin;
        var m = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        return d.Mass <= 8000 && m <= 3 ? 1 : d.Mass <= 52000 && m <= 10 ? 2 : 3;
    }

    /// <summary>
    /// How far a carried piece is tilted at the top of its swing (the wrist's pitch, StationRender): a plate edge first 0.55 rad, a long
    /// piece 0.3, a short one 0.15. <paramref name="size"/> is the piece's whole size (its def's bounds).
    /// </summary>
    public static float TiltOf(Vector3 size)
    {
        var lng = MathF.Max(size.X, size.Z);
        return size.Y < 0.6f && lng > 1.5f ? 0.55f : lng > 3 ? 0.3f : 0.15f;
    }

    /// <summary>How deep a piece hangs under the jaws turned by <paramref name="turn"/> (bay frame) and tilted by <paramref name="tilt"/> about a level axis.</summary>
    public static float HangDepth(Vector3 size, Quaternion turn, float tilt)
    {
        var h = size * 0.5f;
        var worst = 0f;
        for (var ax = 0; ax < 2; ax++)
        {
            var r = Matrix4x4.CreateFromQuaternion(turn) * (ax == 0 ? Matrix4x4.CreateRotationX(tilt) : Matrix4x4.CreateRotationZ(tilt));
            worst = MathF.Max(worst, 2 * (MathF.Abs(r.M12) * h.X + MathF.Abs(r.M22) * h.Y + MathF.Abs(r.M32) * h.Z));
        }
        return worst;
    }

    public static float Ease(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

    public static float Wrap(float a)
    {
        while (a > MathF.PI)
            a -= 2 * MathF.PI;
        while (a < -MathF.PI)
            a += 2 * MathF.PI;
        return a;
    }

    /// <summary>Between two hook offsets round the mast: angle the short way, radius and height straight.</summary>
    public static Vector3 Swing(Vector3 a, Vector3 b, float u)
    {
        float ra = MathF.Sqrt(a.X * a.X + a.Z * a.Z), rb = MathF.Sqrt(b.X * b.X + b.Z * b.Z);
        if (ra < 0.3f || rb < 0.3f)
            return Vector3.Lerp(a, b, u);
        float aa = MathF.Atan2(a.X, a.Z), ab = aa + Wrap(MathF.Atan2(b.X, b.Z) - aa);
        float an = aa + (ab - aa) * u, r = ra + (rb - ra) * u;
        return new Vector3(MathF.Sin(an) * r, a.Y + (b.Y - a.Y) * u, MathF.Cos(an) * r);
    }

    /// <summary>How long the hook takes between two offsets (its arc, then its height).</summary>
    public static float SwingSeconds(in RoverClass rc, Vector3 a, Vector3 b)
    {
        float ra = MathF.Sqrt(a.X * a.X + a.Z * a.Z), rb = MathF.Sqrt(b.X * b.X + b.Z * b.Z);
        var arc = MathF.Abs(Wrap(MathF.Atan2(b.X, b.Z) - MathF.Atan2(a.X, a.Z))) * MathF.Max(1f, (ra + rb) * 0.5f);
        return 0.4f + (arc + MathF.Abs(rb - ra)) / (rc.Speed * HandSpeed) + MathF.Abs(b.Y - a.Y) / VSpeed;
    }

    /// <summary>
    /// The shoulder's height over the bay's floor with the hook <paramref name="hookY"/> up and the trip's carry height <paramref name="up"/>:
    /// the mast telescopes so the arm comes down from over the hull's walls (the lab's tower-crane mast), all the way down while parked
    /// (<paramref name="fold"/> 1: on its pad), and always where the arm can reach the hook. Either peer, the same.
    /// </summary>
    public static float ShoulderAt(in RoverClass rc, float horiz, float hookY, float up, float fold)
    {
        var lo = rc.Shoulder;
        var goal = MathF.Max(hookY + 1f + 3.5f, MathF.Min(up + 2f, hookY + 1f + 12f));
        var y = lo + (1 - fold) * (Math.Clamp(goal, lo, lo + rc.Mast) - lo);
        var r = rc.Reach - 0.3f;
        if (horiz < r)
        {
            var dy = MathF.Sqrt(r * r - horiz * horiz);
            // (A hook higher than the mast reaches puts the low bound over the high one: the mast stands at its top then,
            // never a Clamp with min over max, which threw mid-build and took the game down.)
            var hi = MathF.Max(lo, MathF.Min(lo + rc.Mast, hookY + 1f + dy));
            y = Math.Clamp(y, MathF.Min(hi, MathF.Max(lo, hookY + 1f - dy)), hi);
        }
        return y;
    }

    /// <summary>Does a rover standing at <paramref name="b"/> reach the hook point <paramref name="q"/> (bay frame, the jaws' height)?</summary>
    public static bool Reaches(in RoverClass rc, Vector2 b, Vector3 q)
    {
        var d = Vector2.Distance(b, new Vector2(q.X, q.Z));
        var r = rc.Reach - 0.3f;
        if (d > r)
            return false;
        return q.Y + 1f <= rc.Shoulder + rc.Mast + MathF.Sqrt(r * r - d * d);
    }

    /// <summary>The highest the hook goes over a point <paramref name="d"/> m from where the rover stands.</summary>
    public static float MaxUp(in RoverClass rc, float d)
    {
        var r = rc.Reach - 0.3f;
        return d >= r ? rc.Shoulder + rc.Mast - 1f : rc.Shoulder + rc.Mast - 1f + MathF.Sqrt(r * r - d * d);
    }

    /// <summary>How folded a rover at <paramref name="p"/> is: 1 parked square on its pad, 0 three metres off it.</summary>
    public static float Fold(in StationDock dk, Vector2 p) => 1f - Math.Clamp((Vector2.Distance(p, dk.Mid) - 0.3f) / 3f, 0f, 1f);

    // ---- The docks (either peer, from the parts alone) --------------------------------------------------------------------

    /// <summary>
    /// Station <paramref name="t"/>'s docks into <paramref name="docks"/>, the Core's first: each Dock Expansion on the body whose mouth lies
    /// on the bay's edge and opens into it (the first station's when two bays share an edge). How many.
    /// </summary>
    public static int Docks(Construct c, int t, Span<StationDock> docks)
    {
        if (docks.Length == 0 || ConstructStation.BaySize(c, t) is not { X: > 0 } bay)
            return 0;
        Note(c);
        ConstructStation.Bay(c, t, out var floor, out var rot, out _);
        var inv = Quaternion.Conjugate(rot);
        float hx = bay.X * 0.5f, hz = bay.Z * 0.5f;
        var n = 0;
        if (DockOf(c, t, 1, floor, inv, out var core))
            docks[n++] = core;
        for (var k = 0; k < c.StationDockN && n < docks.Length; k++)
        {
            int p = c.StationDockParts[k];
            if (p >= c.PartCount || c.Parts[p].Broken || !DockOf(c, p, c.DefOf(p).StationDock, floor, inv, out var dk))
                continue;
            var mouth = dk.Mid + dk.Dir * (Classes[dk.Cls].PadLen * 0.5f);
            var onX = MathF.Abs(MathF.Abs(mouth.X) - hx) < 0.35f && MathF.Abs(mouth.Y) <= hz + 0.05f && MathF.Abs(dk.Dir.X) > 0.9f && dk.Dir.X * mouth.X < 0;
            var onZ = MathF.Abs(MathF.Abs(mouth.Y) - hz) < 0.35f && MathF.Abs(mouth.X) <= hx + 0.05f && MathF.Abs(dk.Dir.Y) > 0.9f && dk.Dir.Y * mouth.Y < 0;
            if ((onX || onZ) && !OtherStationTakes(c, t, p))
                docks[n++] = dk;
        }
        return n;
    }

    /// <summary>A dock pad of class <paramref name="cls"/> on part <paramref name="p"/> (its mouth at the part's +x, x = 1) in the bay's frame.</summary>
    private static bool DockOf(Construct c, int p, int cls, Vector3 floor, Quaternion inv, out StationDock dk)
    {
        dk = default;
        if (cls is < 1 or > 3)
            return false;
        Freeform.PartWorld(c, p, out var pos, out var prot);
        var rc = Classes[cls];
        var mid = Vector3.Transform(pos + Vector3.Transform(new Vector3(1f - rc.PadLen * 0.5f, 0, 0), prot) - floor, inv);
        var dir = Vector3.Transform(Vector3.Transform(Vector3.UnitX, prot), inv);
        if (MathF.Abs(mid.Y) > 1.5f || MathF.Abs(dir.Y) > 0.2f)
            return false;
        dk = new StationDock { Part = (ushort)p, Cls = (byte)cls, Mid = new Vector2(mid.X, mid.Z), Dir = Vector2.Normalize(new Vector2(dir.X, dir.Z)) };
        return true;
    }

    /// <summary>Does an earlier station on the body take dock part <paramref name="p"/> (a pad on the edge two bays share goes to the first)?</summary>
    private static bool OtherStationTakes(Construct c, int t, int p)
    {
        for (var a = 0; a < c.PieceRackCount; a++)
        {
            int o = c.PieceRacks[a];
            if (o == t)
                return false;
            if (o >= c.PartCount || !c.DefOf(o).IsBuildStation || ConstructStation.BaySize(c, o) is not { X: > 0 } bay)
                continue;
            ConstructStation.Bay(c, o, out var floor, out var rot, out _);
            if (!DockOf(c, p, c.DefOf(p).StationDock, floor, Quaternion.Conjugate(rot), out var dk))
                continue;
            var mouth = dk.Mid + dk.Dir * (Classes[dk.Cls].PadLen * 0.5f);
            if (MathF.Abs(MathF.Abs(mouth.X) - bay.X * 0.5f) < 0.35f && MathF.Abs(mouth.Y) <= bay.Z * 0.5f + 0.05f
                || MathF.Abs(MathF.Abs(mouth.Y) - bay.Z * 0.5f) < 0.35f && MathF.Abs(mouth.X) <= bay.X * 0.5f + 0.05f)
                return true;
        }
        return false;
    }

    private static void Note(Construct c)
    {
        if (c.StationDockStamp == c.StructStamp && c.StationDockCount == c.PartCount)
            return;
        (c.StationDockStamp, c.StationDockCount, c.StationDockN) = (c.StructStamp, c.PartCount, 0);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).StationDock == 0)
                continue;
            if (c.StationDockParts.Length <= c.StationDockN)
                Array.Resize(ref c.StationDockParts, Math.Max(8, c.StationDockParts.Length * 2));
            c.StationDockParts[c.StationDockN++] = (ushort)i;
        }
    }

    // ---- Where a rover stands and how it drives there (server) ---------------------------------------------------------------

    /// <summary>The ground a rover's middle may stand on: the bay less its half length (so its body stays on the bay whichever way it faces).</summary>
    public struct Ground
    {
        public float Hx, Hz; // the bay's half size
        public Vector2 KLo, KHi; // the build's footprint (bay frame)
        public bool Keep; // a build stands in the bay
        public RoverFloor? Floor; // the parts already fitted, which drives go round and stands keep off (null: the ring alone)

        public readonly void Inset(in RoverClass rc, out float ix, out float iz)
        {
            ix = MathF.Max(0.1f, Hx - rc.HalfLen - 0.3f);
            iz = MathF.Max(0.1f, Hz - rc.HalfLen - 0.3f);
        }

        /// <summary>The keep-out ring's corners for a class: the footprint grown by the rover's half width and a margin.</summary>
        public readonly void Ring(in RoverClass rc, out Vector2 lo, out Vector2 hi)
        {
            var m = rc.HalfWid + 0.6f;
            lo = KLo - new Vector2(m);
            hi = KHi + new Vector2(m);
        }
    }

    private static bool Inside(Vector2 p, Vector2 lo, Vector2 hi) => p.X > lo.X + 0.01f && p.X < hi.X - 0.01f && p.Y > lo.Y + 0.01f && p.Y < hi.Y - 0.01f;

    /// <summary>Straight out to the nearest side of the ring.</summary>
    private static Vector2 ToRing(Vector2 p, Vector2 lo, Vector2 hi)
    {
        float dl = p.X - lo.X, dr = hi.X - p.X, db = p.Y - lo.Y, dt = hi.Y - p.Y;
        var m = MathF.Min(MathF.Min(dl, dr), MathF.Min(db, dt));
        return m == dl ? p with { X = lo.X } : m == dr ? p with { X = hi.X } : m == db ? p with { Y = lo.Y } : p with { Y = hi.Y };
    }

    /// <summary>Where a rover of class <paramref name="rc"/> stands to work at <paramref name="q"/> (bay frame): on the bay, off the build's ring, nearest the point.</summary>
    public static Vector2 SpotFor(in Ground g, in RoverClass rc, Vector3 q)
    {
        g.Inset(rc, out var ix, out var iz);
        var p = new Vector2(Math.Clamp(q.X, -ix, ix), Math.Clamp(q.Z, -iz, iz));
        if (g.Keep)
        {
            g.Ring(rc, out var lo, out var hi);
            if (Inside(p, lo, hi))
            {
                p = ToRing(p, lo, hi);
                p = new Vector2(Math.Clamp(p.X, -ix, ix), Math.Clamp(p.Y, -iz, iz));
            }
        }
        return p;
    }

    /// <summary>
    /// Where a rover stands to work at <paramref name="q"/>: <see cref="SpotFor"/>, off the ring round what is fitted; when that spot
    /// doesn't reach (the ring grew past its reach), the nearest point on the bay inside the ring (a stand over what is fitted is moved
    /// off it after by <see cref="FreeStand"/>), so a piece inside the build is never left for nobody.
    /// </summary>
    public static Vector2 StandFor(in Ground g, in RoverClass rc, Vector3 q)
    {
        var s = SpotFor(g, rc, q);
        if (!g.Keep || Reaches(rc, s, q))
            return s;
        g.Inset(rc, out var ix, out var iz);
        return new Vector2(Math.Clamp(q.X, -ix, ix), Math.Clamp(q.Z, -iz, iz));
    }

    /// <summary>The class number of <paramref name="rc"/> (1 small, 2 medium, 3 large).</summary>
    public static int ClsOf(in RoverClass rc)
    {
        for (var c = 1; c < Classes.Length; c++)
            if (Classes[c].Name == rc.Name)
                return c;
        return 0;
    }

    /// <summary>
    /// A stand off what is already fitted (Sean 2026-11-28): <paramref name="s"/> itself when it is clear, else the nearest clear spot on
    /// the bay within half the arm's reach that still reaches <paramref name="q1"/> and <paramref name="q2"/>; <paramref name="s"/> when
    /// none does (a large rover in a narrow bay stands on the build).
    /// </summary>
    public static Vector2 FreeStand(in Ground g, in RoverClass rc, Vector2 s, Vector3 q1, Vector3 q2)
    {
        var cls = ClsOf(rc);
        if (g.Floor is not { } f || !f.Covered(cls, s))
            return s;
        g.Inset(rc, out var ix, out var iz);
        var max = MathF.Min(8f, rc.Reach * 0.5f);
        for (var r = 0.5f; r <= max; r += 0.5f)
        {
            var steps = Math.Max(8, (int)(r * 2 * MathF.PI / 0.5f));
            var best = s;
            var bestD = float.MaxValue;
            for (var k = 0; k < steps; k++)
            {
                var an = k * 2 * MathF.PI / steps;
                var p = s + new Vector2(MathF.Cos(an), MathF.Sin(an)) * r;
                if (MathF.Abs(p.X) > ix || MathF.Abs(p.Y) > iz || f.Covered(cls, p) || !Reaches(rc, p, q1) || !Reaches(rc, p, q2))
                    continue;
                var d = Vector2.DistanceSquared(p, new Vector2(q1.X, q1.Z)) + Vector2.DistanceSquared(p, new Vector2(q2.X, q2.Z));
                if (d < bestD)
                    (best, bestD) = (p, d);
            }
            if (bestD < float.MaxValue)
                return best;
        }
        return s;
    }

    /// <summary>Does the segment from a to b pass through the ring's inside?</summary>
    private static bool Crosses(Vector2 a, Vector2 b, Vector2 lo, Vector2 hi)
    {
        lo += new Vector2(0.05f);
        hi -= new Vector2(0.05f);
        float t0 = 0, t1 = 1;
        var d = b - a;
        for (var ax = 0; ax < 2; ax++)
        {
            float pa = ax == 0 ? a.X : a.Y, pd = ax == 0 ? d.X : d.Y, l = ax == 0 ? lo.X : lo.Y, h = ax == 0 ? hi.X : hi.Y;
            if (MathF.Abs(pd) < 1e-6f)
            {
                if (pa <= l || pa >= h)
                    return false;
                continue;
            }
            float u0 = (l - pa) / pd, u1 = (h - pa) / pd;
            if (u0 > u1)
                (u0, u1) = (u1, u0);
            t0 = MathF.Max(t0, u0);
            t1 = MathF.Min(t1, u1);
            if (t0 >= t1)
                return false;
        }
        return true;
    }

    /// <summary>The waypoints from a to b round the ring (b last; a left out): at most four corners, the shortest way.</summary>
    public static int Route(in Ground g, in RoverClass rc, Vector2 a, Vector2 b, Span<Vector2> way)
    {
        if (g.Floor != null)
            return g.Floor.Route(ClsOf(rc), a, b, way); // (round what is fitted, over the floor the build hasn't reached yet)
        if (!g.Keep)
        {
            way[0] = b;
            return 1;
        }
        g.Ring(rc, out var lo, out var hi);
        if (!Crosses(a, b, lo, hi))
        {
            way[0] = b;
            return 1;
        }
        g.Inset(rc, out var ix, out var iz);
        Span<Vector2> node = stackalloc Vector2[6];
        node[0] = a;
        node[1] = b;
        node[2] = new Vector2(lo.X, lo.Y);
        node[3] = new Vector2(hi.X, lo.Y);
        node[4] = new Vector2(hi.X, hi.Y);
        node[5] = new Vector2(lo.X, hi.Y);
        for (var k = 2; k < 6; k++)
            node[k] = new Vector2(Math.Clamp(node[k].X, -ix, ix), Math.Clamp(node[k].Y, -iz, iz));
        Span<float> dist = stackalloc float[6];
        Span<int> prev = stackalloc int[6];
        Span<bool> done = stackalloc bool[6];
        dist.Fill(float.MaxValue);
        prev.Fill(-1);
        dist[0] = 0;
        for (var it = 0; it < 6; it++)
        {
            var u = -1;
            for (var k = 0; k < 6; k++)
                if (!done[k] && dist[k] < float.MaxValue && (u < 0 || dist[k] < dist[u]))
                    u = k;
            if (u < 0 || u == 1)
                break;
            done[u] = true;
            for (var v = 0; v < 6; v++)
            {
                if (done[v] || v == u || Crosses(node[u], node[v], lo, hi))
                    continue;
                var nd = dist[u] + Vector2.Distance(node[u], node[v]);
                if (nd < dist[v])
                    (dist[v], prev[v]) = (nd, u);
            }
        }
        if (prev[1] < 0)
        {
            way[0] = b; // (no way round: straight, as the narrow lanes the lab knows)
            return 1;
        }
        Span<int> back = stackalloc int[6];
        var n = 0;
        for (var v = 1; v > 0 && n < 6; v = prev[v])
            back[n++] = v;
        var w = 0;
        for (var k = n - 1; k >= 0 && w < way.Length; k--)
            way[w++] = node[back[k]];
        return w;
    }

    /// <summary>Turn on the spot to face <paramref name="to"/>, then drive straight to it (keys into <paramref name="p"/>).</summary>
    public static void Leg(RoverPlan p, in RoverClass rc, ref float t, ref Vector2 at, ref float h, Vector2 to, bool reverse = false)
    {
        var d = to - at;
        var len = d.Length();
        if (len < 0.05f)
            return;
        if (!reverse)
        {
            var want = MathF.Atan2(d.X, d.Y);
            var turn = Wrap(want - h);
            if (MathF.Abs(turn) > 0.02f)
            {
                h += turn;
                t += MathF.Abs(turn) / rc.Turn + 0.2f;
                p.AddBase(t, at.X, at.Y, h);
            }
        }
        t += len / rc.Speed + 0.4f; // (its start and stop)
        at = to;
        p.AddBase(t, at.X, at.Y, h);
    }

    public const float CurveAngle = 2.1f; // rad: a corner sharper than this (about 120 degrees) is a stop and a turn on the spot, not a curve

    /// <summary>
    /// Drive through the points <paramref name="pts"/> (keys into <paramref name="p"/>), curving through the corners (Sean 2026-11-28):
    /// a turn on the spot to face the first, then each corner rounded in an arc of about one and a half body lengths' radius (less where
    /// the legs are short), keeping its speed through it; a stop only at the end, or at a corner sharper than <see cref="CurveAngle"/>,
    /// where it turns on the spot. A run's time is its length at the rover's speed and 0.4 s for its start and stop. Allocates nothing.
    /// </summary>
    public static void Path(RoverPlan p, in RoverClass rc, ref float t, ref Vector2 at, ref float h, ReadOnlySpan<Vector2> pts)
    {
        Span<Vector2> q = stackalloc Vector2[pts.Length + 1];
        var m = 0;
        var last = at;
        foreach (var v in pts)
            if (Vector2.Distance(v, last) >= 0.05f)
                q[m++] = last = v;
        if (m == 0)
            return;
        var d0 = q[0] - at;
        TurnTo(p, rc, ref t, at, ref h, MathF.Atan2(d0.X, d0.Y));
        Span<Vector2> run = stackalloc Vector2[3 * m + 1];
        Span<float> head = stackalloc float[3 * m + 1];
        var rn = 0;
        var prev = at;
        for (var i = 0; i < m; i++)
        {
            var a = Vector2.Normalize(q[i] - prev);
            if (i == m - 1)
            {
                (run[rn], head[rn]) = (q[i], MathF.Atan2(a.X, a.Y));
                rn++;
                break;
            }
            var b = Vector2.Normalize(q[i + 1] - q[i]);
            var th = MathF.Acos(Math.Clamp(Vector2.Dot(a, b), -1f, 1f));
            float ha = MathF.Atan2(a.X, a.Y), hb = MathF.Atan2(b.X, b.Y);
            if (th > CurveAngle)
            {
                // (a sharp corner: the run ends there and it turns on the spot)
                (run[rn], head[rn]) = (q[i], ha);
                rn++;
                Run(p, rc, ref t, ref at, ref h, run[..rn], head[..rn]);
                rn = 0;
                TurnTo(p, rc, ref t, at, ref h, hb);
            }
            else if (th < 0.02f)
            {
                (run[rn], head[rn]) = (q[i], ha);
                rn++;
            }
            else
            {
                var tan = MathF.Tan(th * 0.5f);
                var d = MathF.Min(rc.HalfLen * 3f * tan, 0.45f * MathF.Min(Vector2.Distance(prev, q[i]), Vector2.Distance(q[i], q[i + 1])));
                var r = d / tan;
                var mid = q[i] + Vector2.Normalize(b - a) * (r / MathF.Cos(th * 0.5f) - r);
                (run[rn], head[rn]) = (q[i] - a * d, ha);
                (run[rn + 1], head[rn + 1]) = (mid, ha + Wrap(hb - ha) * 0.5f);
                (run[rn + 2], head[rn + 2]) = (q[i] + b * d, hb);
                rn += 3;
            }
            prev = q[i];
        }
        Run(p, rc, ref t, ref at, ref h, run[..rn], head[..rn]);
    }

    /// <summary>One run of a drive: its points keyed by distance at the rover's speed, driven on through all but the last.</summary>
    private static void Run(RoverPlan p, in RoverClass rc, ref float t, ref Vector2 at, ref float h, ReadOnlySpan<Vector2> pts, ReadOnlySpan<float> heads)
    {
        if (pts.Length == 0)
            return;
        var len = 0f;
        var from = at;
        foreach (var v in pts)
        {
            len += Vector2.Distance(from, v);
            from = v;
        }
        if (len < 0.05f)
            return;
        var span = len / rc.Speed + 0.4f; // (its start and stop)
        var go = 0f;
        from = at;
        for (var k = 0; k < pts.Length; k++)
        {
            go += Vector2.Distance(from, pts[k]);
            from = pts[k];
            p.AddBase(t + span * go / len, pts[k].X, pts[k].Y, heads[k], smooth: k < pts.Length - 1);
        }
        t += span;
        at = pts[^1];
        h = heads[^1];
    }

    /// <summary>Turn on the spot to <paramref name="want"/>.</summary>
    public static void TurnTo(RoverPlan p, in RoverClass rc, ref float t, Vector2 at, ref float h, float want)
    {
        var turn = Wrap(want - h);
        if (MathF.Abs(turn) <= 0.02f)
            return;
        h += turn;
        t += MathF.Abs(turn) / rc.Turn + 0.2f;
        p.AddBase(t, at.X, at.Y, h);
    }

    /// <summary>
    /// A drive from (<paramref name="at"/>, <paramref name="h"/>) to a stand <paramref name="to"/>, squared up at the end with its short end
    /// towards <paramref name="face"/>: out of its dock straight by its mouth first when it starts parked, round the ring by its corners.
    /// </summary>
    public static void Drive(RoverPlan p, in Ground g, in RoverClass rc, in StationDock dk, ref float t, ref Vector2 at, ref float h, Vector2 to, Vector2 face)
    {
        Span<Vector2> pts = stackalloc Vector2[RoverFloor.MaxWay + 1];
        var m = 0;
        var from = at;
        if (Vector2.Distance(at, dk.Mid) < 0.6f && Vector2.Distance(to, at) > 0.3f)
            pts[m++] = from = MouthOf(dk); // (out of the dock straight first: it faces its mouth parked)
        m += Route(g, rc, from, to, pts[m..]);
        Path(p, rc, ref t, ref at, ref h, pts[..m]);
        var f = face - at;
        if (f.LengthSquared() > 0.25f)
            TurnTo(p, rc, ref t, at, ref h, MathF.Atan2(f.X, f.Y));
    }

    /// <summary>Where a rover clears its dock's mouth: a body's length into the bay, on the pad's centre line.</summary>
    public static Vector2 MouthOf(in StationDock dk)
    {
        var rc = Classes[dk.Cls];
        return dk.Mid + dk.Dir * (rc.PadLen * 0.5f + rc.HalfLen + 0.8f);
    }

    /// <summary>The hook tucked in by the mast, ahead of a rover heading <paramref name="h"/>: its driving pose (drawn folded, StationRender).</summary>
    public static Vector3 StowOf(in RoverClass rc, float h) => new(MathF.Sin(h) * 1.5f, rc.Shoulder + 0.5f, MathF.Cos(h) * 1.5f);

    public const float TuckDrive = 6f; // m: an empty drive longer than this goes with the arm tucked (Sean 2026-11-27: less clipping)

    /// <summary>The trip home: round the ring to its mouth, square to the bay, and back straight onto its pad (it parks facing the bay).</summary>
    public static void PlanHome(RoverPlan p, in Ground g, in StationDock dk, Vector2 at, float h, Vector3 hook, float wait)
    {
        var rc = Classes[dk.Cls];
        p.Clear();
        p.Home = true;
        float t = 0;
        p.AddBase(0, at.X, at.Y, h);
        p.AddHook(0, hook);
        // The arm folds at once, as it sets off (Sean 2026-11-27): its fold was keyed after the wait, so on a long wait it crept to its
        // stow over the whole wait, a raised arm standing in the others' way.
        var stow = StowOf(rc, h);
        p.AddHook(MathF.Min(3f, SwingSeconds(rc, hook, stow)), stow);
        if (wait > 0)
        {
            t = wait;
            p.AddBase(t, at.X, at.Y, h);
        }
        var mouth = MouthOf(dk);
        Span<Vector2> way = stackalloc Vector2[RoverFloor.MaxWay];
        var n = Route(g, rc, at, mouth, way);
        Path(p, rc, ref t, ref at, ref h, way[..n]);
        TurnTo(p, rc, ref t, at, ref h, MathF.Atan2(dk.Dir.X, dk.Dir.Y));
        Leg(p, rc, ref t, ref at, ref h, dk.Mid, reverse: true);
        p.AddHook(t, stow);
        p.Total = t + 0.2f;
    }

    /// <summary>
    /// A carry: from where it stands to the pick (<paramref name="pick"/>, the piece's top, bay frame; null: nothing to pick, a laid part),
    /// up to <paramref name="up"/>, round its mast to the place (<paramref name="place"/>: the part's top where it goes), down, welded for
    /// <paramref name="weld"/> s, let go and up. <paramref name="shift"/> slides the stands along the lane (the avoidance's spot beside);
    /// <paramref name="wait"/> holds the start. False: no stand reaches.
    /// </summary>
    public static bool PlanCarry(RoverPlan p, in Ground g, in StationDock dk, Vector2 at, float h, Vector3 hook, Vector3? pick, Vector3 place, float up, float weld, float wait, int shift)
    {
        var rc = Classes[dk.Cls];
        p.Clear();
        var parked = Vector2.Distance(at, dk.Mid) < 0.6f;
        Vector2 s1, s2;
        var src = pick ?? place;
        var srcUp = src with { Y = up };
        var tgtUp = place with { Y = up };
        // Swinging beats driving: stay put when this spot reaches both; else one stop that reaches both; drive twice only when it must.
        bool Both(Vector2 b) => Reaches(rc, b, srcUp) && Reaches(rc, b, tgtUp) && Reaches(rc, b, src) && Reaches(rc, b, place);
        if (!parked && shift == 0 && Both(at) && g.Floor?.Covered(ClsOf(rc), at) != true)
            s1 = s2 = at;
        else
        {
            s1 = StandFor(g, rc, src);
            s2 = StandFor(g, rc, place);
            if (shift != 0)
            {
                s1 = Slide(g, rc, s1, shift);
                s2 = Slide(g, rc, s2, shift);
            }
            if (Both(s1))
                s2 = s1;
            else if (Both(s2))
                s1 = s2;
            // (off what is already fitted, where a spot near that still reaches: Sean 2026-11-28)
            if (s1 == s2)
            {
                var f = FreeStand(g, rc, s1, src, place);
                if (Both(f))
                    s1 = s2 = f;
            }
            else
            {
                s1 = FreeStand(g, rc, s1, src, srcUp);
                s2 = FreeStand(g, rc, s2, place, tgtUp);
            }
        }
        if (!Reaches(rc, s1, src) || !Reaches(rc, s2, place) || !Reaches(rc, s1, srcUp) || !Reaches(rc, s2, tgtUp))
            return false;
        float t = 0;
        p.AddBase(0, at.X, at.Y, h);
        p.AddHook(0, hook);
        if (wait > 0)
        {
            t = wait;
            p.AddBase(t, at.X, at.Y, h);
            p.AddHook(t, hook);
        }
        var t0 = t;
        var pos = at;
        var head = h;
        Drive(p, g, rc, dk, ref t, ref pos, ref head, s1, new Vector2(src.X, src.Z));
        var aboveSrc = srcUp - new Vector3(s1.X, 0, s1.Y);
        float tArr;
        if (Vector2.Distance(at, s1) > TuckDrive)
        {
            // (a long empty drive: the arm tucks in as it sets off and reaches out again as it comes in, not held out over the others)
            var stow = StowOf(rc, h);
            var tIn = t0 + MathF.Min(3f, SwingSeconds(rc, hook, stow));
            p.AddHook(tIn, stow);
            var sOut = SwingSeconds(rc, stow, aboveSrc);
            var tOut = MathF.Max(tIn, t - sOut);
            p.AddHook(tOut, stow);
            tArr = MathF.Max(t, tOut + sOut);
        }
        else
            tArr = MathF.Max(t, t0 + SwingSeconds(rc, hook, aboveSrc));
        p.AddHook(tArr, aboveSrc);
        t = tArr;
        if (pick is { } q)
        {
            var low = q - new Vector3(s1.X, 0, s1.Y);
            var dDown = MathF.Max(0.3f, (up - q.Y) / DropSpeed);
            p.AddHook(t += dDown, low);
            p.GripT = t += GripSeconds;
            p.AddHook(t, low);
            p.AddHook(t += dDown, aboveSrc);
        }
        // To the fitting spot (driving with the piece held out, the hook swinging round as it goes) and over the place.
        var leave = t;
        pos = s1;
        Drive(p, g, rc, dk, ref t, ref pos, ref head, s2, new Vector2(place.X, place.Z));
        var aboveTgt = tgtUp - new Vector3(s2.X, 0, s2.Y);
        t = MathF.Max(t, leave + SwingSeconds(rc, aboveSrc, aboveTgt));
        p.AddBase(t, pos.X, pos.Y, head);
        p.AddHook(t, aboveTgt);
        var at2 = place - new Vector3(s2.X, 0, s2.Y);
        var dPlace = MathF.Max(0.3f, (up - place.Y) / DropSpeed);
        p.AddHook(t += dPlace, at2);
        p.FitT = t += weld;
        p.AddHook(t, at2);
        p.AddHook(t += ReleaseSeconds, at2);
        p.AddHook(t += MathF.Max(0.3f, dPlace * 0.6f), at2 with { Y = MathF.Min(up, place.Y + 2.5f) - 0f });
        p.AddBase(t, pos.X, pos.Y, head);
        p.Up = up;
        p.Total = t + 0.1f;
        if (pick == null)
            p.GripT = p.FitT - weld - dPlace; // (a laid part: it appears in the jaws over the place)
        return true;
    }

    /// <summary>A stand slid along the lane it stands in by <paramref name="k"/> rover widths (the avoidance's spot beside), kept on the bay.</summary>
    private static Vector2 Slide(in Ground g, in RoverClass rc, Vector2 s, int k)
    {
        g.Inset(rc, out var ix, out var iz);
        var d = k * (2 * rc.Radius + 0.8f);
        var alongZ = true;
        if (g.Keep)
        {
            g.Ring(rc, out var lo, out var hi);
            var offX = MathF.Max(lo.X - s.X, s.X - hi.X);
            var offZ = MathF.Max(lo.Y - s.Y, s.Y - hi.Y);
            alongZ = offX >= offZ; // (beside the ring's long side: slide along it)
        }
        else
            alongZ = g.Hz >= g.Hx;
        var n = alongZ ? s with { Y = s.Y + d } : s with { X = s.X + d };
        return new Vector2(Math.Clamp(n.X, -ix, ix), Math.Clamp(n.Y, -iz, iz));
    }

    /// <summary>Does a rover's middle at <paramref name="p"/> come within <paramref name="r"/> of the body of the rover parked on dock <paramref name="dk"/> (its box on the pad)?</summary>
    public static bool ParkedClash(in StationDock dk, Vector2 p, float r)
    {
        var rc = Classes[dk.Cls];
        var o = p - dk.Mid;
        var along = MathF.Abs(Vector2.Dot(o, dk.Dir)) - rc.HalfLen;
        var across = MathF.Abs(o.X * dk.Dir.Y - o.Y * dk.Dir.X) - rc.HalfWid;
        float dx = MathF.Max(0, along), dy = MathF.Max(0, across);
        return dx * dx + dy * dy < r * r;
    }

    /// <summary>Do two rovers' bodies meet (circles; two rovers each parked on its own pad never do)?</summary>
    public static bool Clash(in StationDock da, Vector2 a, in StationDock db, Vector2 b, float pad)
    {
        if (Vector2.Distance(a, da.Mid) < 0.6f && Vector2.Distance(b, db.Mid) < 0.6f)
            return false;
        var r = Classes[da.Cls].Radius + Classes[db.Cls].Radius + pad;
        return Vector2.DistanceSquared(a, b) < r * r;
    }
}
