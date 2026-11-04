using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// A freeform track (docs/sandbox/CONSTRUCTS.md, "Tracks"): a loop of links round some of a construct's wheels, like a belt on
/// pulleys. It holds only which wheels and its settings; the shape is the convex hull of the wheels' circles, worked out when
/// needed (<see cref="TrackLoop"/>). No link is a body: a track is one set of contact rays, one shared speed and one drive.
/// </summary>
public struct CTrack
{
    public ushort Start; // its wheels in Construct.TrackParts
    public byte Count;
    public float Width, LinkLength, Tension, Grip; // the settings: metres, metres, 0 slack..1 taut (how much it sags), friction
    public float Health; // 0: shot off (the wheels run on as plain wheels until it is mended)

    // Rebuilt with the caches
    public bool Live; // it wraps its wheels this moment: whole, every wheel where it should be
    public byte Seg; // the segment its wheels hang on (0, or a steering pivot's)
    public Vector3 Axle, Along; // rest frame: the wheels' axle and the way across it; a point is (Along, Y) in the loop's plane
    public float Plane; // where its middle sits along the axle
    public float MaxHealth;
    public Vector3 Min, Max; // rest frame: its box round the wheels (a ray's quick test)

    // Server state, each tick
    public float Speed; // m/s of the track over the wheels (positive: forward)
    public float Drive; // this tick's push on the ground, all its wheels' together (an impulse)
    public float GripSum; // last tick: the friction its contacts had between them
    public int Contacts; // last tick: how many contacts touched
    public float Sum, Free; // the tick's working: the contacts' speeds together; the fastest wheel in the air
}

/// <summary>One wheel of a loop in the loop's plane.</summary>
public struct LoopWheel
{
    public float U, V, R;
}

/// <summary>One stretch of a loop: the arc round wheel <see cref="Wheel"/> up to angle <see cref="Leave"/>, then the straight run from A to B to the next wheel.</summary>
public struct LoopRun
{
    public int Wheel;
    public float Leave, Turn; // the straight's direction, and how far the arc before it turns (radians)
    public float Ax, Ay, Bx, By, Len;
}

/// <summary>
/// The shape of a track: a band pulled tight round circles is the convex hull of the circles, a closed run of arcs and
/// straights. A gift wrap from the lowest wheel, going counter-clockwise (the bottom run goes +U), so the outside is on the
/// right of the way it goes. A wheel inside the hull doesn't touch the band; a big wheel can be touched twice. Nothing allocates.
/// </summary>
public static class TrackLoop
{
    /// <summary>Wraps <paramref name="ws"/> into <paramref name="runs"/> (room for twice the wheels and two more). The number of runs: 0 for no wheel, 1 for one (or all inside one).</summary>
    public static int Wrap(ReadOnlySpan<LoopWheel> ws, Span<LoopRun> runs)
    {
        if (ws.Length == 0)
            return 0;
        var start = 0;
        for (var i = 1; i < ws.Length; i++)
            if (ws[i].V - ws[i].R < ws[start].V - ws[start].R - 1e-5f || MathF.Abs(ws[i].V - ws[i].R - (ws[start].V - ws[start].R)) <= 1e-5f && ws[i].R > ws[start].R)
                start = i;
        var at = start;
        var angle = 0f; // going +U along the bottom of the lowest wheel
        var total = 0f;
        var n = 0;
        var most = Math.Min(runs.Length, ws.Length * 2 + 2);
        while (n < most)
        {
            var best = -1;
            var bestDelta = float.MaxValue;
            var bestAngle = 0f;
            var bestDist = 0f;
            ref readonly var a = ref ws[at];
            for (var j = 0; j < ws.Length; j++)
            {
                if (j == at)
                    continue;
                ref readonly var b = ref ws[j];
                var dx = b.U - a.U;
                var dy = b.V - a.V;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= MathF.Abs(a.R - b.R) + 1e-5f)
                    continue; // (one inside the other)
                var th = MathF.Atan2(dy, dx) + MathF.Asin((a.R - b.R) / d);
                var delta = (th - angle) % MathF.Tau;
                if (delta < -1e-5f)
                    delta += MathF.Tau;
                delta = MathF.Max(0, delta);
                if (delta < bestDelta - 1e-5f || delta <= bestDelta + 1e-5f && d > bestDist)
                {
                    best = j;
                    bestDelta = delta;
                    bestAngle = th;
                    bestDist = d;
                }
            }
            if (best < 0 || total + bestDelta >= MathF.Tau + 1e-3f)
                break;
            ref readonly var to = ref ws[best];
            var (sin, cos) = MathF.SinCos(bestAngle);
            ref var r = ref runs[n++];
            r.Wheel = at;
            r.Leave = bestAngle;
            r.Turn = bestDelta;
            r.Ax = a.U + a.R * sin;
            r.Ay = a.V - a.R * cos;
            r.Bx = to.U + to.R * sin;
            r.By = to.V - to.R * cos;
            r.Len = MathF.Sqrt((r.Bx - r.Ax) * (r.Bx - r.Ax) + (r.By - r.Ay) * (r.By - r.Ay));
            total += bestDelta;
            angle = bestAngle;
            at = best;
            if (at == start)
            {
                // Round again: the arc left on the first wheel, back to where it set off.
                var left = (runs[0].Leave - angle) % MathF.Tau;
                if (left < 0)
                    left += MathF.Tau;
                runs[0].Turn = left;
                return n;
            }
        }
        if (n == 0)
        {
            // One wheel (or all inside it): a loop round it.
            ref var r = ref runs[n++];
            r.Wheel = start;
            r.Leave = 0;
            r.Turn = MathF.Tau;
            r.Ax = r.Bx = ws[start].U;
            r.Ay = r.By = ws[start].V - ws[start].R;
            r.Len = 0;
        }
        return n;
    }

    /// <summary>How long the band is.</summary>
    public static float Length(ReadOnlySpan<LoopWheel> ws, ReadOnlySpan<LoopRun> runs)
    {
        var l = 0f;
        foreach (ref readonly var r in runs)
            l += r.Len + r.Turn * ws[r.Wheel].R;
        return l;
    }

    /// <summary>The loop's lowest point at <paramref name="u"/> (every circle and straight lies in the hull, so the lowest of them is its edge), or +infinity outside it.</summary>
    public static float Lower(ReadOnlySpan<LoopWheel> ws, ReadOnlySpan<LoopRun> runs, float u)
    {
        var low = float.PositiveInfinity;
        foreach (ref readonly var w in ws)
        {
            var d = u - w.U;
            if (MathF.Abs(d) <= w.R)
                low = MathF.Min(low, w.V - MathF.Sqrt(MathF.Max(0, w.R * w.R - d * d)));
        }
        foreach (ref readonly var r in runs)
            if (r.Bx - r.Ax > 1e-5f && u >= r.Ax && u <= r.Bx)
                low = MathF.Min(low, r.Ay + (r.By - r.Ay) * (u - r.Ax) / (r.Bx - r.Ax));
        return low;
    }
}

/// <summary>A construct's tracks (fixed arrays that only grow on an edit: nothing allocates as it is simulated).</summary>
public sealed partial class Construct
{
    public const int MaxTracks = 64;
    public const int MaxTrackWheels = 48; // on one track

    public CTrack[] Tracks = [];
    public int TrackCount;
    public ushort[] TrackParts = []; // the tracks' wheels (parts)
    public ushort[] TrackWheel = []; // rebuilt: beside TrackParts, each one's entry in Wheels (ushort.MaxValue: none)
    public int TrackPartCount;
    public byte[] TrackOf = []; // rebuilt, by part: the live track it is on (plus one)
    public bool TrackOfUsed;
    public bool TrackUnits; // rebuilt: has Track parts (a wheel with a "track" block)
    public int LeadCount; // rebuilt: wheels and rays off the ground at rest (a track's raised stretches), which carry no share of the weight

    internal void ReserveTracks(int tracks, int parts)
    {
        if (Tracks.Length < tracks)
            Array.Resize(ref Tracks, Math.Max(4, tracks * 2));
        if (TrackParts.Length < parts)
        {
            Array.Resize(ref TrackParts, Math.Max(16, parts * 2));
            Array.Resize(ref TrackWheel, TrackParts.Length);
        }
    }

    /// <summary>The live track part <paramref name="part"/> is on, or -1.</summary>
    public int TrackAt(int part) => part < TrackOf.Length ? TrackOf[part] - 1 : -1;

    /// <summary>The track (live or shot off) that holds part <paramref name="part"/>, or -1.</summary>
    public int TrackHolding(int part)
    {
        for (var t = 0; t < TrackCount; t++)
            for (var k = 0; k < Tracks[t].Count; k++)
                if (TrackParts[Tracks[t].Start + k] == part)
                    return t;
        return -1;
    }
}

/// <summary>Tracks: the rules, the store's upkeep (saves, the wire, renumbering) and the contact rays. The tick is in <see cref="ConstructPhysics"/>, below.</summary>
public static class ConstructTracks
{
    public const float MinWidth = 0.1f, MaxWidth = 40f, MinLink = 0.05f, MaxLink = 8f, MinGrip = 0.2f, MaxGrip = 2f;
    public const float DefaultTension = 0.7f, DefaultGrip = 1.05f; // (the Track part's grip)
    public const float OffroadGrip = 1.4f, SideGrip = 0.4f; // a track's bite on dirt (times grip), and sideways
    public const float NoSpringHz = 3.2f, NoSpringDamp = 0.8f; // a wheel on no suspension still gives a little (the track's own pads)
    public const int MaxStations = 12; // contact rays between a track's wheels
    public const float MaxLoop = 1600f; // metres round

    public const float TurnGrip = 0.45f; // a skid turn: the sideways friction a track's pads have, of their grip (they slide across the ground more easily than they drive)
    public const float ClimbRate = 6f; // /s: how fast a contact past its travel lifts the body, for each metre it is past (a share of its slew at most)
    public const float TurnGripWheel = 0.15f; // the same for a driven tyre on a vehicle with a steering differential
    public const float TurnGripFree = 0.03f; // and for a free one: it can't resist the turn its driven wheels make
    public const float TurnSpeedPivots = 3f; // the grip is back at this share of this speed with pivots too: they steer, or the car spins out
    public const float PivotFade = 8f; // m/s: with pivots too, the sides' share of the steering is down to its least by here
    public const float ClimbDrive = 1f, ClimbSpeed = 6f, ClimbFrom = 0.12f, ClimbFull = 0.5f; // up a grade a track pulls this much more, fading out by this speed (m/s), from this sine of the grade to full at that
    public const float BiteGrip = 0.5f; // a track's pads bite on a grade: this much more grip at 45 degrees, in proportion to the sine of the grade
    public const float ParkSpeed = 0.25f, ParkDecel = 2.5f; // m/s, m/s2: throttle released, a track parks below this and runs down to it at that
    public const float TurnSpeed = 12f; // m/s: the sideways grip is back in full at this share of it (0.55 of this speed)
    public const float TurnBrake = 0.015f; // how hard the inner track is held back, turning on the move
    public const float Slew = 2.4f; // m/s: how fast a track's contact follows a step (a big one's suspension sets its own)

    /// <summary>A track's sag at full slack, as a share of a top run's length (Tension 0).</summary>
    public const float SlackSag = 0.1f;

    // ---- The rules -------------------------------------------------------------------------------------------------

    /// <summary>A wheel's width along its axle, from its boxes.</summary>
    public static float WheelWidth(Construct c, int part, Vector3 axle)
    {
        c.PartBounds(part, out var mn, out var mx);
        return MathF.Abs(Vector3.Dot(mx - mn, axle));
    }

    /// <summary>The axle of wheel part <paramref name="part"/> in the rest frame.</summary>
    public static bool AxleOf(Construct c, int part, out Vector3 axle)
    {
        var d = c.DefOf(part);
        for (var k = 0; k < d.Sockets.Length; k++)
            if (d.Sockets[k].Kind == SocketKind.Axle)
            {
                axle = ConstructParts.DirVector(Rot24.ApplyDir(c.Parts[part].Rot, d.Sockets[k].Dir));
                return true;
            }
        axle = Vector3.UnitX;
        return false;
    }

    /// <summary>Could these parts, in this order, take a track? Null, or why not (said on the HUD and by the server alike). <paramref name="ignore"/>: a track of theirs that doesn't count.</summary>
    public static string? Check(Construct c, ReadOnlySpan<ushort> parts, int ignore = -1)
    {
        if (parts.Length > Construct.MaxTrackWheels)
            return $"a track goes round at most {Construct.MaxTrackWheels} wheels";
        if (c.TrackCount >= Construct.MaxTracks && ignore < 0)
            return $"a construct has at most {Construct.MaxTracks} tracks";
        if (parts.Length == 0)
            return "pick a wheel";
        Span<LoopWheel> ws = stackalloc LoopWheel[parts.Length];
        var axle = Vector3.Zero;
        var along = Vector3.Zero;
        var seg = 0;
        var plane = 0f;
        var thin = float.MaxValue;
        for (var k = 0; k < parts.Length; k++)
        {
            int i = parts[k];
            if (i >= c.PartCount)
                return "that part is gone";
            var d = c.DefOf(i);
            if (d.Role != PartRole.Wheel)
                return $"a {d.Name} isn't a wheel";
            if (d.TrackRays > 0)
                return $"a {d.Name} is a whole track already: use wheels";
            for (var o = 0; o < k; o++)
                if (parts[o] == i)
                    return "that wheel is in the loop already";
            var on = c.TrackHolding(i);
            if (on >= 0 && on != ignore)
                return "that wheel is on another track";
            if (!AxleOf(c, i, out var a))
                return $"a {d.Name} has no axle";
            if (MathF.Abs(a.Y) > 0.5f)
                return "that wheel lies flat: a track runs on wheels stood up";
            int s = c.Parts[i].Seg;
            if (s != 0 && (s > c.JointCount || c.Joints[s - 1].Type != JointType.Steering || c.Joints[s - 1].ParentSeg != 0))
                return "that wheel hangs on a joint or a freeform attachment: a track's wheels sit on the body, or all on one steering pivot";
            c.PartBounds(i, out var mn, out var mx);
            var centre = (mn + mx) * 0.5f;
            if (k == 0)
            {
                axle = a;
                along = Vector3.Cross(a, Vector3.UnitY);
                seg = s;
                plane = Vector3.Dot(centre, a);
            }
            else
            {
                if (MathF.Abs(Vector3.Dot(a, axle)) < 0.99f)
                    return "that wheel turns another way: a track's wheels share one axle direction";
                if (s != seg)
                    return "those wheels don't move together: all on the body, or all on one steering pivot";
            }
            thin = MathF.Min(thin, WheelWidth(c, i, a));
            if (MathF.Abs(Vector3.Dot(centre, axle) - plane) > MathF.Max(0.1f, thin))
                return "that wheel isn't in line with the others";
            ws[k] = new LoopWheel { U = Vector3.Dot(centre, along), V = centre.Y, R = d.WheelRadius };
        }
        if (parts.Length < 2)
            return "a track needs two wheels or more";
        Span<LoopRun> runs = stackalloc LoopRun[parts.Length * 2 + 2];
        var n = TrackLoop.Wrap(ws, runs);
        if (n < 2)
            return "those wheels are one inside another";
        if (TrackLoop.Length(ws, runs[..n]) > MaxLoop)
            return $"a track is at most {MaxLoop:0} m round";
        return null;
    }

    /// <summary>A new track's width: across all its wheels.</summary>
    public static float DefaultWidth(Construct c, ReadOnlySpan<ushort> parts)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        if (parts.Length == 0 || parts[0] >= c.PartCount || !AxleOf(c, parts[0], out var a))
            return MinWidth;
        foreach (var p in parts)
            if (p < c.PartCount)
            {
                c.PartBounds(p, out var mn, out var mx);
                var (x, y) = (Vector3.Dot(mn, a), Vector3.Dot(mx, a));
                lo = MathF.Min(lo, MathF.Min(x, y));
                hi = MathF.Max(hi, MathF.Max(x, y));
            }
        return Math.Clamp(MathF.Round((hi - lo) * 1.04f, 2), MinWidth, MaxWidth);
    }

    /// <summary>A new track's link length: a share of its smallest wheel.</summary>
    public static float DefaultLink(Construct c, ReadOnlySpan<ushort> parts)
    {
        var r = float.MaxValue;
        foreach (var p in parts)
            if (p < c.PartCount)
                r = MathF.Min(r, c.DefOf(p).WheelRadius);
        return Math.Clamp(MathF.Round(r * 0.45f, 2), MinLink, MaxLink);
    }

    /// <summary>Adds a track round <paramref name="parts"/> (checked already). Its index.</summary>
    public static int Add(Construct c, ReadOnlySpan<ushort> parts, float width, float link, float tension, float grip, float health = -1)
    {
        c.ReserveTracks(c.TrackCount + 1, c.TrackPartCount + parts.Length);
        ref var t = ref c.Tracks[c.TrackCount];
        t = new CTrack { Start = (ushort)c.TrackPartCount, Count = (byte)parts.Length, Width = width, LinkLength = link, Tension = tension, Grip = grip, Health = health };
        for (var k = 0; k < parts.Length; k++)
            c.TrackParts[c.TrackPartCount++] = parts[k];
        return c.TrackCount++;
    }

    /// <summary>Takes track <paramref name="track"/> off (its wheels stay).</summary>
    public static void Remove(Construct c, int track)
    {
        ref var t = ref c.Tracks[track];
        int gone = t.Count, start = t.Start;
        Array.Copy(c.TrackParts, start + gone, c.TrackParts, start, c.TrackPartCount - start - gone);
        c.TrackPartCount -= gone;
        for (var k = track + 1; k < c.TrackCount; k++)
        {
            c.Tracks[k - 1] = c.Tracks[k];
            c.Tracks[k - 1].Start -= (ushort)gone;
        }
        c.TrackCount--;
    }

    /// <summary>
    /// After parts were renumbered (<paramref name="map"/>: old index to new, -1 gone). On the same construct a wheel that went
    /// leaves the loop (one wheel left: the track goes); a piece split off takes the tracks wholly on it.
    /// </summary>
    public static void Remap(Construct src, ReadOnlySpan<int> map, Construct dst)
    {
        var same = src == dst;
        if (src.TrackCount == 0)
            return;
        Span<ushort> kept = stackalloc ushort[Construct.MaxTrackWheels];
        if (same)
        {
            var write = 0;
            var tn = 0;
            for (var k = 0; k < src.TrackCount; k++)
            {
                var t = src.Tracks[k];
                var n = 0;
                for (var i = 0; i < t.Count; i++)
                {
                    int was = src.TrackParts[t.Start + i];
                    if (was < map.Length && map[was] >= 0)
                        kept[n++] = (ushort)map[was];
                }
                if (n < 2)
                    continue;
                t.Start = (ushort)write;
                t.Count = (byte)n;
                for (var i = 0; i < n; i++)
                    src.TrackParts[write++] = kept[i];
                src.Tracks[tn++] = t;
            }
            src.TrackCount = tn;
            src.TrackPartCount = write;
            return;
        }
        for (var k = 0; k < src.TrackCount; k++)
        {
            ref var t = ref src.Tracks[k];
            var all = true;
            for (var i = 0; i < t.Count && all; i++)
            {
                int was = src.TrackParts[t.Start + i];
                all = was < map.Length && map[was] >= 0;
                if (all)
                    kept[i] = (ushort)map[was];
            }
            if (all && dst.TrackCount < Construct.MaxTracks)
                Add(dst, kept[..t.Count], t.Width, t.LinkLength, t.Tension, t.Grip, t.Health);
        }
    }

    /// <summary>Copies <paramref name="b"/>'s tracks into <paramref name="a"/> (a join: b's parts now start at <paramref name="off"/>).</summary>
    public static void Merge(Construct a, Construct b, int off)
    {
        Span<ushort> moved = stackalloc ushort[Construct.MaxTrackWheels];
        for (var k = 0; k < b.TrackCount && a.TrackCount < Construct.MaxTracks; k++)
        {
            ref var t = ref b.Tracks[k];
            for (var i = 0; i < t.Count; i++)
                moved[i] = (ushort)(b.TrackParts[t.Start + i] + off);
            Add(a, moved[..t.Count], t.Width, t.LinkLength, t.Tension, t.Grip, t.Health);
        }
    }

    // ---- The caches ------------------------------------------------------------------------------------------------

    /// <summary>With the caches, before the colliders: which tracks are live, and where each wheel of theirs is. A track whose wheels no longer fit (bent off by an edit) is dead until mended.</summary>
    internal static void Rebuild(Construct c)
    {
        if (c.TrackOf.Length < c.PartCount)
            c.TrackOf = new byte[Math.Max(16, c.Parts.Length)];
        else if (c.TrackOfUsed)
            Array.Clear(c.TrackOf);
        c.TrackOfUsed = c.TrackCount > 0;
        if (c.TrackCount == 0)
            return;
        Span<LoopWheel> ws = stackalloc LoopWheel[Construct.MaxTrackWheels];
        Span<LoopRun> runs = stackalloc LoopRun[Construct.MaxTrackWheels * 2 + 2];
        for (var k = 0; k < c.TrackCount; k++)
        {
            ref var t = ref c.Tracks[k];
            var parts = c.TrackParts.AsSpan(t.Start, t.Count);
            var bad = Check(c, parts, k);
            t.Live = bad == null;
            if (bad != null)
                continue;
            AxleOf(c, parts[0], out t.Axle);
            t.Along = Vector3.Cross(t.Axle, Vector3.UnitY);
            t.Seg = c.Parts[parts[0]].Seg;
            t.Min = new Vector3(float.MaxValue);
            t.Max = new Vector3(float.MinValue);
            float lo = float.MaxValue, hi = float.MinValue;
            for (var i = 0; i < parts.Length; i++)
            {
                c.PartBounds(parts[i], out var mn, out var mx);
                t.Min = Vector3.Min(t.Min, mn);
                t.Max = Vector3.Max(t.Max, mx);
                var centre = (mn + mx) * 0.5f;
                var (x, y) = (Vector3.Dot(mn, t.Axle), Vector3.Dot(mx, t.Axle));
                lo = MathF.Min(lo, MathF.Min(x, y));
                hi = MathF.Max(hi, MathF.Max(x, y));
                ws[i] = new LoopWheel { U = Vector3.Dot(centre, t.Along), V = centre.Y, R = c.DefOf(parts[i]).WheelRadius };
            }
            t.Plane = (lo + hi) * 0.5f;
            var pad = new Vector3(t.Width * 0.5f + 0.1f);
            t.Min -= pad;
            t.Max += pad;
            var n = TrackLoop.Wrap(ws[..parts.Length], runs);
            t.MaxHealth = FullHealth(t.Width, TrackLoop.Length(ws[..parts.Length], runs[..n]));
            if (t.Health < 0 || t.Health > t.MaxHealth)
                t.Health = t.MaxHealth;
            if (t.Health <= 0)
            {
                t.Live = false;
                continue;
            }
            for (var i = 0; i < parts.Length; i++)
                c.TrackOf[parts[i]] = (byte)(k + 1);
        }
    }

    /// <summary>How much a track takes before it comes off: more with its size, not with every link.</summary>
    public static float FullHealth(float width, float length) => 80f + 60f * width * MathF.Sqrt(MathF.Max(1, length));

    /// <summary>A track's wheels as they stand at rest (<paramref name="ws"/>: room for its wheels), from the built wheel list. Their count.</summary>
    internal static int RestWheels(Construct c, in CTrack t, Span<LoopWheel> ws)
    {
        var n = 0;
        for (var i = 0; i < t.Count; i++)
        {
            int k = c.TrackWheel[t.Start + i];
            if (k >= c.WheelCount)
                continue;
            ref var w = ref c.Wheels[k];
            ws[n++] = new LoopWheel { U = Vector3.Dot(w.Centre, t.Along), V = w.Centre.Y - w.Travel * 0.5f, R = w.Radius };
        }
        return n;
    }

    /// <summary>
    /// The contact rays along each track, once its wheels are in the wheel list: along the lower edge of the loop, from one end to
    /// the other, a ray every so far (by the run's length), each one a wheel to the physics: its own spring, on the nearest wheel's
    /// suspension, reaching the band's edge there. Those on a rising stretch (a raised front run, the curve round an end wheel)
    /// are "lead" rays: they touch nothing on the flat, carry no share of the weight, and meet a step before the body does.
    /// </summary>
    internal static void BuildStations(Construct c, ReadOnlySpan<float> keep, int old)
    {
        Span<LoopWheel> ws = stackalloc LoopWheel[Construct.MaxTrackWheels];
        Span<LoopRun> runs = stackalloc LoopRun[Construct.MaxTrackWheels * 2 + 2];
        for (var k = 0; k < c.TrackCount; k++)
        {
            ref var t = ref c.Tracks[k];
            if (!t.Live)
                continue;
            var n = RestWheels(c, t, ws);
            if (n < 2)
                continue;
            var wheels = ws[..n];
            var count = TrackLoop.Wrap(wheels, runs);
            var loop = runs[..count];
            float umin = float.MaxValue, umax = float.MinValue, floor = float.MaxValue, rSum = 0;
            foreach (ref readonly var w in wheels)
            {
                umin = MathF.Min(umin, w.U - w.R);
                umax = MathF.Max(umax, w.U + w.R);
                floor = MathF.Min(floor, w.V - w.R);
                rSum += w.R;
            }
            var rMean = rSum / n;
            var flat = MathF.Max(0.01f, 0.06f * rMean); // (within this of the lowest: on the ground at rest)
            // A wheel well above the ground run is a lead too.
            for (var i = 0; i < t.Count; i++)
            {
                int wk = c.TrackWheel[t.Start + i];
                if (wk >= c.WheelCount)
                    continue;
                ref var m = ref c.Wheels[wk];
                if (m.Centre.Y - m.Travel * 0.5f - m.Radius > floor + flat)
                {
                    m.Lead = true;
                    c.LeadCount++;
                }
            }
            var spacing = MathF.Max(rMean * 0.6f, (umax - umin) / (MaxStations + 1));
            var rays = Math.Clamp((int)MathF.Ceiling((umax - umin) / spacing), 2, MaxStations + 1);
            var inset = MathF.Min(0.12f * rMean, (umax - umin) * 0.1f);
            for (var r = 0; r <= rays && c.WheelCount < Construct.MaxWheels; r++)
            {
                var u = umin + inset + (umax - umin - 2 * inset) * r / rays;
                var low = TrackLoop.Lower(wheels, loop, u);
                if (!float.IsFinite(low))
                    continue;
                // The nearest wheel lends its suspension; a ray right under a wheel adds nothing.
                var near = -1;
                var nearD = float.MaxValue;
                for (var i = 0; i < t.Count; i++)
                {
                    int wk = c.TrackWheel[t.Start + i];
                    if (wk >= c.WheelCount)
                        continue;
                    var d = MathF.Abs(Vector3.Dot(c.Wheels[wk].Centre, t.Along) - u);
                    if (d < nearD)
                    {
                        nearD = d;
                        near = wk;
                    }
                }
                if (near < 0)
                    continue;
                ref readonly var from = ref c.Wheels[near];
                var lead = low > floor + flat;
                if (!lead && nearD < spacing * 0.3f && !from.Lead)
                    continue;
                if (c.WheelCount == c.Wheels.Length)
                    Array.Resize(ref c.Wheels, Math.Min(Construct.MaxWheels, Math.Max(8, c.WheelCount * 2)));
                ref var s = ref c.Wheels[c.WheelCount];
                s = from;
                s.Station = true;
                s.Lead = lead;
                s.ArmOut = s.ArmAxis = Vector3.Zero;
                s.ArmLen = 0;
                s.Centre = t.Along * u + t.Axle * t.Plane + new Vector3(0, low + from.Radius + from.Travel * 0.5f, 0);
                s.Compression = c.WheelCount < old ? keep[c.WheelCount] : 0;
                if (lead)
                {
                    s.Travel *= 0.5f; // (it reaches the band's edge and no further: no droop, so on the flat it touches nothing)
                    c.LeadCount++;
                }
                c.WheelCount++;
            }
        }
    }

    /// <summary>In the wheel list: a wheel takes its track's tyre, and on no suspension a little give of the track's own.</summary>
    internal static void Join(Construct c, ref CWheel w, int part, byte loop, bool sprung)
    {
        ref var t = ref c.Tracks[loop - 1];
        w.Loop = loop;
        w.Track = true;
        w.Grip = t.Grip * c.DefOf(part).Friction;
        w.Offroad = OffroadGrip;
        w.Side = SideGrip;
        w.Band = Thickness(t.LinkLength);
        for (var i = 0; i < t.Count; i++)
            if (c.TrackParts[t.Start + i] == part)
                c.TrackWheel[t.Start + i] = (ushort)c.WheelCount;
        if (sprung)
            return;
        var give = Math.Clamp(w.Radius * 0.14f, 0.03f, 2f);
        w.Centre.Y += give * 0.5f;
        w.Travel = give;
        w.Frequency = NoSpringHz;
        w.Damping = NoSpringDamp;
        w.MaxLoad = float.MaxValue;
    }

    /// <summary>
    /// A Track part's lead rays: past its last flat ray, the curve up round each end: two rays that touch nothing on the flat
    /// and carry no share of the weight, and meet a kerb before the part is over it, so it rides up like a track's nose.
    /// </summary>
    internal static void LeadRays(Construct c, Vector3 along, Vector3 centre, ConstructPartDef d, ReadOnlySpan<float> keep, int old)
    {
        var r = d.WheelRadius;
        var last = c.WheelCount - 1;
        for (var side = -1; side <= 1; side += 2)
            for (var k = 0; k < 2 && c.WheelCount < Construct.MaxWheels; k++)
            {
                var past = r * (k == 0 ? 0.5f : 0.9f); // (how far round the end's circle)
                if (c.WheelCount == c.Wheels.Length)
                    Array.Resize(ref c.Wheels, Math.Min(Construct.MaxWheels, Math.Max(8, c.WheelCount * 2)));
                ref var w = ref c.Wheels[c.WheelCount];
                w = c.Wheels[last];
                w.Station = w.Lead = true;
                w.Centre += along * (side * (d.TrackLength * 0.5f - r + past) - Vector3.Dot(w.Centre - centre, along));
                w.Centre.Y += r - MathF.Sqrt(r * r - past * past);
                w.Travel *= 0.5f; // (it reaches the band's edge and no further: no droop, so on the flat it touches nothing)
                w.Compression = c.WheelCount < old ? keep[c.WheelCount] : 0;
                c.LeadCount++;
                c.WheelCount++;
            }
    }

    /// <summary>
    /// Where a Track part is drawn against its place at rest: up its travel and tilted, by a straight line through its flat rays,
    /// so it lies on the ground along its length (wheel <paramref name="first"/> is its first ray).
    /// </summary>
    public static Matrix4x4 UnitPose(Construct c, int part, int first)
    {
        ref var f = ref c.Wheels[first];
        var along = Vector3.Cross(f.Axle, Vector3.UnitY);
        float n = 0, su = 0, sc = 0, suu = 0, suc = 0;
        for (var k = first; k < c.WheelCount && c.Wheels[k].Part == part; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Lead)
                continue;
            var u = Vector3.Dot(w.Centre, along);
            var rise = w.Compression - w.Travel * 0.5f;
            n++;
            su += u;
            sc += rise;
            suu += u * u;
            suc += u * rise;
        }
        if (n == 0)
            return Matrix4x4.Identity;
        var mean = sc / n;
        var spread = suu - su * su / n;
        var slope = spread > 1e-4f ? (suc - su * sc / n) / spread : 0;
        c.PartBounds(part, out var mn, out var mx);
        var hub = (mn + mx) * 0.5f;
        return Matrix4x4.CreateTranslation(-hub) * Matrix4x4.CreateFromAxisAngle(f.Axle, -MathF.Atan(slope)) * Matrix4x4.CreateTranslation(hub + new Vector3(0, mean, 0));
    }

    // ---- Damage ----------------------------------------------------------------------------------------------------

    /// <summary>Damage to a track. At zero it comes off: the wheels run on as plain wheels. Returns true if it came off.</summary>
    public static bool Damage(ConstructWorld? world, Construct c, int track, float damage)
    {
        if (track < 0 || track >= c.TrackCount || damage <= 0)
            return false;
        ref var t = ref c.Tracks[track];
        if (t.Health <= 0)
            return false;
        t.Health -= damage;
        c.Wake();
        if (t.Health > 0)
            return false;
        t.Health = 0;
        if (world != null)
            world.Disable(c);
        else
            c.Damaged = true;
        Changed(world, c);
        world?.Events.Add($"construct {c.Id}: a track was shot off");
        return true;
    }

    /// <summary>A blast near a construct's tracks: each takes the damage at its nearest wheel, fading to the radius.</summary>
    public static void Blast(ConstructWorld world, Construct c, Vector3 centre, float radius, float maxDamage)
    {
        for (var k = c.TrackCount - 1; k >= 0; k--)
        {
            ref var t = ref c.Tracks[k];
            if (!t.Live)
                continue;
            var near = float.MaxValue;
            for (var i = 0; i < t.Count; i++)
            {
                c.PartBounds(c.TrackParts[t.Start + i], out var mn, out var mx);
                near = MathF.Min(near, Vector3.Distance(c.ToWorld((mn + mx) * 0.5f), centre));
            }
            if (near < radius)
                Damage(world, c, k, maxDamage * (1 - near / radius));
        }
    }

    public const string Material = "metal"; // what links are made of in survival
    public const int LinkUnits = 1; // units of it a link

    /// <summary>What these many links cost in survival.</summary>
    public static int Cost(int links) => Math.Max(1, links * LinkUnits);

    /// <summary>How many links of <paramref name="link"/> m go round these wheels as they stand (the whole loop, in any state of the track).</summary>
    public static int LinkCount(Construct c, ReadOnlySpan<ushort> parts, float link)
    {
        if (parts.Length < 2 || parts.Length > Construct.MaxTrackWheels)
            return 1;
        AxleOf(c, parts[0], out var axle);
        var along = Vector3.Cross(axle, Vector3.UnitY);
        Span<LoopWheel> ws = stackalloc LoopWheel[Construct.MaxTrackWheels];
        Span<LoopRun> runs = stackalloc LoopRun[Construct.MaxTrackWheels * 2 + 2];
        for (var i = 0; i < parts.Length; i++)
        {
            c.PartBounds(parts[i], out var mn, out var mx);
            var centre = (mn + mx) * 0.5f;
            ws[i] = new LoopWheel { U = Vector3.Dot(centre, along), V = centre.Y, R = c.DefOf(parts[i]).WheelRadius };
        }
        var n = TrackLoop.Wrap(ws[..parts.Length], runs);
        return Math.Max(1, (int)MathF.Ceiling(TrackLoop.Length(ws[..parts.Length], runs[..n]) / MathF.Max(link, MinLink)));
    }

    /// <summary>At full health: nothing to mend.</summary>
    public static bool Whole(Construct c, int track)
    {
        if (track < 0 || track >= c.TrackCount)
            return true;
        ref var t = ref c.Tracks[track];
        return t.Health >= t.MaxHealth && t.MaxHealth > 0;
    }

    /// <summary>What mending this track costs in survival: the share of its links its lost health stands for, all of them for one shot off.</summary>
    public static int MendCost(Construct c, int track)
    {
        ref var t = ref c.Tracks[track];
        var lost = t.MaxHealth > 0 ? Math.Clamp(1 - t.Health / t.MaxHealth, 0, 1) : 1;
        return Cost((int)MathF.Ceiling(lost * LinkCount(c, c.TrackParts.AsSpan(t.Start, t.Count), t.LinkLength)));
    }

    /// <summary>Back to full health, and back on. True if anything was mended.</summary>
    public static bool Repair(ConstructWorld? world, Construct c, int track)
    {
        if (Whole(c, track))
            return false;
        ref var t = ref c.Tracks[track];
        t.Health = -1; // (the rebuild sets it to what this track holds)
        Changed(world, c);
        return true;
    }

    internal static void Changed(ConstructWorld? world, Construct c)
    {
        c.Dirty = true;
        c.Wake();
        world?.StructureDirty.Add(c.Id);
    }

    /// <summary>The nearest stretch of track along a ray (world), within <paramref name="best"/>: the band's outer face, and its sides. The track's index, or -1.</summary>
    public static int Ray(Construct c, Vector3 origin, Vector3 dir, ref float best, out Vector3 point)
    {
        point = default;
        var found = -1;
        Span<LoopWheel> ws = stackalloc LoopWheel[Construct.MaxTrackWheels];
        Span<LoopRun> runs = stackalloc LoopRun[Construct.MaxTrackWheels * 2 + 2];
        for (var k = 0; k < c.TrackCount; k++)
        {
            ref var t = ref c.Tracks[k];
            if (!t.Live)
                continue;
            c.WorldFrame(t.Seg, out var q, out var pos);
            var inv = Quaternion.Conjugate(q);
            var o = Vector3.Transform(origin - pos, inv);
            var d = Vector3.Transform(dir, inv);
            if (!RayBox(o, d, t.Min, t.Max, best))
                continue;
            var n = LiveWheels(c, t, ws);
            var count = TrackLoop.Wrap(ws[..n], runs);
            float ou = Vector3.Dot(o, t.Along), ov = o.Y, ow = Vector3.Dot(o, t.Axle) - t.Plane;
            float du = Vector3.Dot(d, t.Along), dv = d.Y, dw = Vector3.Dot(d, t.Axle);
            var half = t.Width * 0.5f;
            var thick = Thickness(t.LinkLength);
            for (var r = 0; r < count; r++)
            {
                ref readonly var run = ref runs[r];
                if (run.Len > 1e-3f)
                {
                    var (ex, ey) = ((run.Bx - run.Ax) / run.Len, (run.By - run.Ay) / run.Len);
                    var (nx, ny) = (ey, -ex); // (outward)
                    // Its outer face.
                    var dn = du * nx + dv * ny;
                    if (dn < -1e-5f)
                    {
                        var s = ((run.Ax + nx * thick - ou) * nx + (run.Ay + ny * thick - ov) * ny) / dn;
                        if (s >= 0 && s < best)
                        {
                            var along = (ou + du * s - run.Ax) * ex + (ov + dv * s - run.Ay) * ey;
                            if (along >= 0 && along <= run.Len && MathF.Abs(ow + dw * s) <= half)
                            {
                                best = s;
                                found = k;
                            }
                        }
                    }
                    // Its two sides.
                    if (MathF.Abs(dw) > 1e-5f)
                        for (var side = -1; side <= 1; side += 2)
                        {
                            var s = (side * half - ow) / dw;
                            if (s < 0 || s >= best)
                                continue;
                            float px = ou + du * s - run.Ax, py = ov + dv * s - run.Ay;
                            var along = px * ex + py * ey;
                            var deep = thick - (px * nx + py * ny);
                            if (along >= 0 && along <= run.Len && deep >= 0 && deep <= thick * 2)
                            {
                                best = s;
                                found = k;
                            }
                        }
                }
            }
            if (found == k)
                point = origin + dir * best;
        }
        return found;
    }

    private static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float reach)
    {
        float lo = 0, hi = reach;
        for (var a = 0; a < 3; a++)
        {
            float oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z, da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            float mn = a == 0 ? min.X : a == 1 ? min.Y : min.Z, mx = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-7f)
            {
                if (oa < mn || oa > mx)
                    return false;
                continue;
            }
            var t0 = (mn - oa) / da;
            var t1 = (mx - oa) / da;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            lo = MathF.Max(lo, t0);
            hi = MathF.Min(hi, t1);
            if (lo > hi)
                return false;
        }
        return true;
    }

    /// <summary>A Track part's links: a share of its end wheels.</summary>
    public static float UnitLink(float radius) => radius * 0.42f;

    /// <summary>
    /// Throttle released under walking pace: how much of a stop a track takes this tick. It parks only once it is all but still (that
    /// also holds it on a slope: from rest a tick of gravity stays under <see cref="ParkSpeed"/>); faster, it runs down at
    /// <see cref="ParkDecel"/>, plus what a slope adds, not to a dead stop in one tick.
    /// </summary>
    public static float Settle(float speed, float slope, float dt)
    {
        var v = MathF.Abs(speed);
        return v <= ParkSpeed ? 1f : MathF.Min(1, (ParkDecel + MathF.Abs(slope)) * dt / v);
    }

    /// <summary>
    /// A track's help on a hill (user, a change to the wheel system, not a collider): how much harder it pulls up a grade of sine
    /// <paramref name="grade"/> (the way it is driven; 0 or less: downhill or level, none) at <paramref name="speed"/> m/s. A track
    /// has a low range of its own, the speed it gives up for it being what a wheel on the same engine keeps.
    /// </summary>
    public static float ClimbBoost(float grade, float speed)
    {
        var steep = Math.Clamp((grade - ClimbFrom) / (ClimbFull - ClimbFrom), 0, 1);
        return 1 + ClimbDrive * steep * Math.Clamp(1 - speed / ClimbSpeed, 0, 1);
    }

    /// <summary>How much better a track grips ground whose normal has this height: its pads dig in, on a slope more than on the flat.</summary>
    public static float GradeGrip(float normalY) => 1 + BiteGrip * MathF.Sqrt(MathF.Max(0, 1 - normalY * normalY)) * 1.414f;

    /// <summary>The band's thickness, from its link length.</summary>
    public static float Thickness(float link) => Math.Clamp(link * 0.42f, 0.03f, 1.6f);

    /// <summary>A track's wheels where they are now, each down its suspension (<paramref name="ws"/>: room for its wheels). Their count.</summary>
    public static int LiveWheels(Construct c, in CTrack t, Span<LoopWheel> ws)
    {
        var n = 0;
        for (var i = 0; i < t.Count; i++)
        {
            int k = c.TrackWheel[t.Start + i];
            if (k >= c.WheelCount)
                continue;
            ref var w = ref c.Wheels[k];
            c.HubMotion(k, out var shift, out _);
            var at = w.Centre - new Vector3(0, w.Travel * 0.5f, 0) + shift;
            ws[n++] = new LoopWheel { U = Vector3.Dot(at, t.Along), V = at.Y, R = w.Radius };
        }
        return n;
    }

    // ---- The wire and the blueprints -------------------------------------------------------------------------------

    /// <summary>A construct's tracks on the wire: which wheels, the settings, the health.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.U8((byte)c.TrackCount);
        for (var k = 0; k < c.TrackCount; k++)
        {
            ref var t = ref c.Tracks[k];
            w.U8(t.Count);
            for (var i = 0; i < t.Count; i++)
                w.U16(c.TrackParts[t.Start + i]);
            w.F32(t.Width).F32(t.LinkLength).F32(t.Tension).F32(t.Grip).F32(t.Health);
        }
    }

    /// <summary>Reads <see cref="Write"/>'s bytes (after the parts: every index is checked against them).</summary>
    public static void Read(ref PacketReader r, Construct c, int parts)
    {
        int n = r.U8();
        if (n > Construct.MaxTracks)
            throw new MalformedPacketException($"a construct of {n} tracks");
        c.TrackCount = c.TrackPartCount = 0;
        Span<ushort> list = stackalloc ushort[Construct.MaxTrackWheels];
        for (var k = 0; k < n; k++)
        {
            int count = r.U8();
            if (count > Construct.MaxTrackWheels)
                throw new MalformedPacketException($"a track of {count} wheels");
            for (var i = 0; i < count; i++)
            {
                list[i] = r.U16();
                if (list[i] >= parts)
                    throw new MalformedPacketException("a track on a part that isn't there");
            }
            float width = r.F32(), link = r.F32(), tension = r.F32(), grip = r.F32(), health = r.F32();
            if (!float.IsFinite(width + link + tension + grip + health))
                throw new MalformedPacketException("a track's settings aren't numbers");
            Add(c, list[..count], Math.Clamp(width, MinWidth, MaxWidth), Math.Clamp(link, MinLink, MaxLink), Math.Clamp(tension, 0, 1), Math.Clamp(grip, MinGrip, MaxGrip), health);
        }
    }

    /// <summary>A construct's tracks in a blueprint: each by its wheels' steps.</summary>
    public static string Json(Construct c)
    {
        var sb = new StringBuilder("[\n");
        for (var k = 0; k < c.TrackCount; k++)
        {
            ref var t = ref c.Tracks[k];
            sb.Append("    { \"wheels\": [");
            for (var i = 0; i < t.Count; i++)
                sb.Append(i > 0 ? ", " : "").Append(c.TrackParts[t.Start + i]);
            sb.Append(FormattableString.Invariant($"], \"width\": {t.Width:0.###}, \"link\": {t.LinkLength:0.###}, \"tension\": {t.Tension:0.###}, \"grip\": {t.Grip:0.###} }}"));
            sb.Append(k + 1 < c.TrackCount ? ",\n" : "\n");
        }
        return sb.Append("  ]").ToString();
    }

    /// <summary>A blueprint's track: its wheels by step, and its settings.</summary>
    public sealed record Spec(int[] Wheels, float Width, float Link, float Tension, float Grip);

    /// <summary>Reads one entry of a blueprint's "tracks".</summary>
    public static Spec Parse(System.Text.Json.JsonElement e, string file, int index)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Object || !e.TryGetProperty("wheels", out var ws) || ws.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new ConstructPartException($"{file}: tracks[{index}] needs \"wheels\": a list of steps");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("wheels" or "width" or "link" or "tension" or "grip"))
                throw new ConstructPartException($"{file}: tracks[{index}]: unknown \"{p.Name}\"");
        var list = new int[ws.GetArrayLength()];
        var n = 0;
        foreach (var v in ws.EnumerateArray())
            list[n++] = v.GetInt32();
        float Opt(string key, float dflt) => e.TryGetProperty(key, out var v) ? v.GetSingle() : dflt;
        return new Spec(list, Opt("width", 0), Opt("link", 0), Opt("tension", DefaultTension), Opt("grip", DefaultGrip));
    }

    /// <summary>Puts a blueprint's track on (its steps turned into parts): the same rules as the tool. Null, or why not.</summary>
    public static string? Build(Construct c, Spec s, ReadOnlySpan<int> partOf)
    {
        Span<ushort> parts = stackalloc ushort[Math.Min(s.Wheels.Length, Construct.MaxTrackWheels)];
        if (s.Wheels.Length > Construct.MaxTrackWheels)
            return $"a track goes round at most {Construct.MaxTrackWheels} wheels";
        for (var k = 0; k < parts.Length; k++)
        {
            if (s.Wheels[k] < 0 || s.Wheels[k] >= partOf.Length)
                return $"no step {s.Wheels[k]}";
            parts[k] = (ushort)partOf[s.Wheels[k]];
        }
        if (Check(c, parts) is { } why)
            return why;
        Add(c, parts, s.Width > 0 ? Math.Clamp(s.Width, MinWidth, MaxWidth) : DefaultWidth(c, parts), s.Link > 0 ? Math.Clamp(s.Link, MinLink, MaxLink) : DefaultLink(c, parts),
            Math.Clamp(s.Tension, 0, 1), Math.Clamp(s.Grip, MinGrip, MaxGrip));
        c.Dirty = true;
        return null;
    }
}

public static partial class ConstructPhysics
{
    /// <summary>
    /// Before the wheels: each track's drive for this tick, every wheel's on it together, whichever of them touches the ground:
    /// a driven sprocket held off the ground drives the whole track. The contacts share it by their grip.
    /// </summary>
    private static void TracksDrive(Construct c, bool driving, float throttle, float steer, float want, float boost, float dt)
    {
        for (var n = 0; n < c.TrackCount; n++)
        {
            ref var t = ref c.Tracks[n];
            if (!t.Live)
                continue;
            t.Drive = 0;
            for (var i = 0; i < t.Count; i++)
            {
                int k = c.TrackWheel[t.Start + i];
                if (k >= c.WheelCount)
                    continue;
                ref var w = ref c.Wheels[k];
                if (driving && w.Share > 0)
                {
                    var dir = w.SideDrive ? Math.Clamp(throttle - steer * w.SideSign, -1, 1) : MathF.Sign(throttle);
                    var amount = !w.SideDrive ? 1f : want > 0 ? MathF.Abs(dir) / want : 0f;
                    t.Drive += MathF.Sign(dir) * amount * ConstructDrive.WheelTorque(c, k) / w.Radius * dt * boost;
                }
                if (w.MotorTorque > 0)
                    t.Drive += HubDrive(c, ref w, t.Speed, dt) * boost;
            }
        }
    }

    /// <summary>A contact's share of its track's drive, by the grip it has this tick.</summary>
    private static float TrackPush(Construct c, in CWheel w, float grip)
    {
        ref var t = ref c.Tracks[w.Loop - 1];
        return t.GripSum > 0 ? t.Drive * MathF.Min(1, grip / t.GripSum) : t.Drive / MathF.Max(1, t.Contacts);
    }

    /// <summary>
    /// After the wheels: the band is one piece, so all its wheels turn at one speed: what the contacts make of the ground, or in the
    /// air what the engine makes of a driven wheel. A wheel held off the ground counts as touching (it feeds the band: the
    /// drive line's load and its share follow the track), and what the drawn links and the wheels' spin follow.
    /// </summary>
    private static void TracksShare(Construct c, float dt)
    {
        for (var n = 0; n < c.TrackCount; n++)
        {
            ref var t = ref c.Tracks[n];
            t.GripSum = 0;
            t.Contacts = 0;
            t.Sum = t.Free = 0;
        }
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Loop == 0)
                continue;
            ref var t = ref c.Tracks[w.Loop - 1];
            if (w.Touching)
            {
                t.GripSum += w.LastGrip;
                t.Contacts++;
                t.Sum += w.Omega * w.Radius;
            }
            else if (!w.Station && MathF.Abs(w.Omega * w.Radius) > MathF.Abs(t.Free))
                t.Free = w.Omega * w.Radius;
        }
        for (var n = 0; n < c.TrackCount; n++)
        {
            ref var t = ref c.Tracks[n];
            t.Speed = t.Contacts > 0 ? t.Sum / t.Contacts : t.Free;
        }
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Loop == 0)
                continue;
            ref var t = ref c.Tracks[w.Loop - 1];
            w.Omega = t.Speed / w.Radius;
            w.Spin += w.Omega * dt;
            if (t.Contacts > 0 && !w.Station && !w.Touching)
                w.Touching = w.Fed = true;
        }
    }

    /// <summary>
    /// A Track part is one track: an open differential sees one wheel, not its rays. Before this, one ray over a dip made it pass
    /// a tenth of the drive (a wheel in the air) and the least-loaded ray capped the rest: a crawler crept over rough ground at 2 m/s.
    /// While any of a unit's rays touches, all of them count as touching, and those on the ground pass on the share of those that aren't.
    /// </summary>
    private static void TrackUnitsShare(Construct c)
    {
        for (var k = 0; k < c.WheelCount;)
        {
            ref var f = ref c.Wheels[k];
            if (!f.Track || f.Loop != 0)
            {
                k++;
                continue;
            }
            int part = f.Part, end = k, rays = 0, down = 0;
            for (; end < c.WheelCount && c.Wheels[end].Part == part; end++)
                if (!c.Wheels[end].Lead)
                {
                    rays++;
                    if (c.Wheels[end].Touching)
                        down++;
                }
            var scale = down > 0 ? rays / (float)down : 1f;
            for (var r = k; r < end; r++)
                if (!c.Wheels[r].Lead)
                {
                    c.Wheels[r].UnitScale = scale;
                    if (down > 0 && !c.Wheels[r].Touching)
                        c.Wheels[r].Touching = c.Wheels[r].Fed = true;
                }
            k = end;
        }
    }
}
