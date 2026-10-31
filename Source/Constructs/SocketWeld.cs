using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a socket weld puts the piece (board item dev-sandbox-socket-weld-3step-sw01, decision 26): a socket of a part of
/// the construct welded to, or a point on a flat face of one of its parts (<see cref="SocketWeld.FacePoint"/>: sockets guide,
/// they don't limit). <see cref="At"/> and <see cref="Dir"/> are the face point's place and outward facing in that part's
/// segment rest frame (unused for a real socket, which says its own).
/// </summary>
public struct WeldTarget
{
    public ushort Body;
    public byte Part, Socket;
    public Vector3 At;
    public byte Dir;

    public readonly bool Face => Socket == SocketWeld.FacePoint;
}

/// <summary>What a socket marker shows (its colour): free, in use, the picked source, or a target the piece can go on.</summary>
public enum WeldMarkerState : byte
{
    Free,
    Used,
    Selected,
    Target,
}

/// <summary>One socket marker of the welder's socket-weld mode: a dot and a short arrow along the socket's facing (world).</summary>
public struct WeldMarker
{
    public ushort Body;
    public byte Part, Socket;
    public Vector3 At, Dir;
    public WeldMarkerState State;
    public float Fade, Off; // alpha share (distance), and how far off the crosshair's ray (the sine of the angle)
}

/// <summary>
/// The welder's three-step socket weld (board item dev-sandbox-socket-weld-3step-sw01, decision 26; docs/sandbox/CONSTRUCTS.md,
/// "Socket weld"), the rules shared by the client (markers, ghost, green or red with the reason) and the server (which
/// checks the final pose): pick a source socket on the piece to move (a loose body or a separate construct), pick a target
/// socket (or a point on a flat face) on the construct, spin about the socket axis. The piece's socket lands on the target
/// point, facing opposite the target's normal, turned by the angle about that normal. Multiples of 90 degrees onto a real
/// socket keep it on the construct's Rot24 grid (the two merge, ConstructJoin.cs); any other angle, a face point, or a
/// target on a moving or freeform segment joins it as a fixed-pose freeform segment (ConstructFreeform.cs). Nothing here
/// allocates.
/// </summary>
public static class SocketWeld
{
    public const byte FacePoint = 255; // WeldTarget.Socket: a point on a flat face, not a socket
    public const int MaxMarkers = 64; // markers shown at once (the nearest the crosshair)
    public const int MaxBodies = 4; // the aimed body and the bodies nearest the crosshair
    public const float ShowRange = 7f; // metres: markers fade out by here
    public const float PickSine = 0.07f; // a marker within about 4 degrees of the crosshair is picked (the magnet)
    public const float NearSine = 0.25f; // bodies within about 14 degrees of the crosshair (plus their size) show markers
    public const float Step = 15f, FineStep = 5f; // degrees: the spin's snaps (Ctrl: the fine ones; Shift: free)
    public const float AngleUnit = 0.01f; // degrees: the angle on the wire (u16, 0..35999)
    public const float Grid = 0.01f; // metres: a face point's grid

    /// <summary>The angle as it goes on the wire (and as both ends use it): 0..360, to <see cref="AngleUnit"/>.</summary>
    public static ushort WireAngle(float degrees)
    {
        var a = MathF.Round(Wrap(degrees) / AngleUnit);
        return (ushort)(a >= 36000 ? 0 : a);
    }

    public static float FromWire(ushort a) => a % 36000 * AngleUnit;

    public static float Wrap(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0;
        var a = degrees % 360f;
        return a < 0 ? a + 360f : a;
    }

    /// <summary>Is the angle a multiple of 90 degrees (the grid's)?</summary>
    public static bool OnGrid(float degrees)
    {
        var q = Wrap(degrees) / 90f;
        return MathF.Abs(q - MathF.Round(q)) < 0.0005f;
    }

    /// <summary>The spin's snapped angle: free (Shift), 5 degree steps (Ctrl), else 15.</summary>
    public static float Snap(float raw, bool free, bool fine)
    {
        if (free)
            return Wrap(raw);
        var s = fine ? FineStep : Step;
        return Wrap(MathF.Round(raw / s) * s);
    }

    /// <summary>A direction square to <paramref name="dir"/> (the spin's 0 mark).</summary>
    public static byte PerpDir(byte dir) => (byte)((dir / 2 + 1) % 3 * 2);

    /// <summary>The source socket in its body's frame: on the piece's main body (segment 0) only. False: no such socket.</summary>
    public static bool SourceFrame(Construct m, int part, int socket, out Vector3 at, out byte dir)
    {
        at = default;
        dir = 0;
        if (part < 0 || part >= m.PartCount || socket < 0 || socket >= m.DefOf(part).Sockets.Length || m.Parts[part].Seg != 0)
            return false;
        m.SocketAt(part, socket, out at, out dir);
        return true;
    }

    /// <summary>The target point and its outward facing in the target part's segment rest frame. False: no such target.</summary>
    public static bool TargetFrame(Construct t, in WeldTarget w, out Vector3 at, out byte dir)
    {
        at = w.At;
        dir = w.Dir;
        if (w.Part >= t.PartCount)
            return false;
        if (w.Face)
            return dir < 6 && float.IsFinite(at.X + at.Y + at.Z);
        if (w.Socket >= t.DefOf(w.Part).Sockets.Length)
            return false;
        t.SocketAt(w.Part, w.Socket, out at, out dir);
        return true;
    }

    /// <summary>Angle 0's turn: the first of the four turns taking the source facing to face the target, upright first (ConstructBuild.Place's order).</summary>
    public static byte BaseTurn(byte sourceDir, byte targetDir)
    {
        Span<byte> turns = stackalloc byte[4];
        var n = Rot24.Matching(sourceDir, (byte)(targetDir ^ 1), turns);
        byte best = 0;
        var bestUp = float.MinValue;
        for (var k = 0; k < n; k++)
        {
            var up = SmartRotation.OldUpright(turns[k]);
            if (up > bestUp)
            {
                bestUp = up;
                best = turns[k];
            }
        }
        return best;
    }

    /// <summary>The piece's body turn in the target segment's frame at <paramref name="degrees"/> about the target's facing.</summary>
    public static Quaternion Turn(byte sourceDir, byte targetDir, float degrees) =>
        Quaternion.Normalize(Quaternion.CreateFromAxisAngle(ConstructParts.DirVector(targetDir), Wrap(degrees) * MathF.PI / 180f) * Rot24.Quat(BaseTurn(sourceDir, targetDir)));

    /// <summary>
    /// Where the piece goes: its body frame in the target part's segment rest frame (<paramref name="relPos"/>,
    /// <paramref name="relRot"/>) and in the world (<paramref name="pos"/>, <paramref name="rot"/>). False: a socket is gone.
    /// </summary>
    public static bool Pose(Construct mover, int part, int socket, Construct target, in WeldTarget w, float degrees,
        out Vector3 relPos, out Quaternion relRot, out Vector3 pos, out Quaternion rot)
    {
        relPos = pos = default;
        relRot = rot = Quaternion.Identity;
        if (!SourceFrame(mover, part, socket, out var sAt, out var sDir) || !TargetFrame(target, w, out var tAt, out var tDir))
            return false;
        relRot = Turn(sDir, tDir, degrees);
        relPos = tAt - Vector3.Transform(sAt, relRot);
        if (OnRotGrid(mover, part, socket, target, w, degrees))
        {
            relRot = Rot24.Quat(Rot24.Nearest(relRot, out _));
            relPos = ConstructWorld.Quantise(relPos); // (on the grid the parts sit on the centimetre grid, as an attached part does)
        }
        target.WorldFrame(target.Parts[w.Part].Seg, out var qs, out var ts);
        rot = Quaternion.Normalize(qs * relRot);
        pos = ts + Vector3.Transform(relPos, qs);
        return true;
    }

    /// <summary>The angle (0, 90, 180 or 270) whose turn is nearest the piece's body turn <paramref name="bodyTurn"/> (the target frame).</summary>
    public static float AngleOf(byte sourceDir, byte targetDir, Quaternion bodyTurn)
    {
        var best = 0f;
        var bestDot = -1f;
        for (var k = 0; k < 4; k++)
        {
            var d = MathF.Abs(Quaternion.Dot(Turn(sourceDir, targetDir, k * 90f), bodyTurn));
            if (d > bestDot)
            {
                bestDot = d;
                best = k * 90f;
            }
        }
        return best;
    }

    /// <summary>
    /// Does this weld stay on the construct's Rot24 grid (the piece merges in, linked socket to socket)? A multiple of 90
    /// degrees, onto a real free socket of the same kind on the construct's main body, from the piece's main body.
    /// Otherwise it joins as a freeform segment.
    /// </summary>
    public static bool OnRotGrid(Construct mover, int part, int socket, Construct target, in WeldTarget w, float degrees)
    {
        if (!OnGrid(degrees) || w.Face || w.Part >= target.PartCount || target.Parts[w.Part].Seg != 0 || part < 0 || part >= mover.PartCount)
            return false;
        var td = target.DefOf(w.Part);
        var md = mover.DefOf(part);
        return w.Socket < td.Sockets.Length && socket < md.Sockets.Length && td.Sockets[w.Socket].Kind == md.Sockets[socket].Kind;
    }

    /// <summary>
    /// The physical rules (the client's preview and the server's check; who may is the server's,
    /// <see cref="ConstructEdits.BeginSocketWeld"/>). Null, or why not.
    /// </summary>
    public static string? Check(ConstructWorld world, IWorldQuery? q, Construct mover, int part, int socket, Construct target, in WeldTarget w, float degrees, ReadOnlySpan<Vector3> feet)
    {
        if (mover == target)
            return "that's the same body: pick a socket on the construct to weld it to";
        if (part < 0 || part >= mover.PartCount || w.Part >= target.PartCount)
            return "that part is gone";
        if (mover.Parts[part].Broken || target.Parts[w.Part].Broken)
            return "that part is wrecked";
        if (mover.Parts[part].Seg != 0)
            return "pick a socket on the piece's main body (not on a joint)";
        if (socket < 0 || socket >= mover.DefOf(part).Sockets.Length)
            return "no such socket";
        if (mover.SocketUsed(part, socket))
            return "that socket is in use: pick a free one";
        if (!w.Face && (w.Socket >= target.DefOf(w.Part).Sockets.Length || target.SocketUsed(w.Part, w.Socket)))
            return w.Socket >= target.DefOf(w.Part).Sockets.Length ? "no such socket" : "something is already attached there";
        if (Freeform.Grounded(mover))
            return "that piece is held to the ground: pick it as the construct to weld to instead";
        if (mover.CarriedBy != 0)
            return "it rides in a cargo volume: let it go first";
        if (mover.CargoCount > 0)
            return "that piece carries cargo: let the cargo go first";
        if (world.CanMerge(target, mover) is { } merge)
            return merge;
        if (!OnRotGrid(mover, part, socket, target, w, degrees))
        {
            // A fixed-pose freeform segment (ConstructFreeform.cs): its limits.
            if (mover.WheelCount > 0)
                return "that piece has wheels: weld it at a 90-degree step onto a socket (wheels roll in the body's own frame)";
            if (target.FreeformCount + mover.FreeformCount + 1 > Freeform.MaxPerConstruct)
                return $"at most {Freeform.MaxPerConstruct} freeform welds a construct";
            if (target.JointCount + mover.JointCount + 1 > Construct.MaxJoints)
                return $"together they'd have more than {Construct.MaxJoints} joints and freeform welds";
            if (world.TotalJoints + 1 > world.JointCap)
                return "the world has too many joints";
        }
        if (!Pose(mover, part, socket, target, w, degrees, out _, out _, out var pos, out var rot))
            return "that socket is gone";
        return Overlap(world, q, mover, target, pos, rot, feet);
    }

    /// <summary>Would the piece at world pose <paramref name="pos"/> / <paramref name="rot"/> overlap the construct, the map or a player? Null, or why.</summary>
    public static string? Overlap(ConstructWorld world, IWorldQuery? q, Construct mover, Construct target, Vector3 pos, Quaternion rot, ReadOnlySpan<Vector3> feet)
    {
        if (target.Dirty)
            target.Rebuild();
        for (var i = 0; i < mover.PartCount; i++)
        {
            var d = mover.DefOf(i);
            mover.SegPose(mover.Parts[i].Seg, out var sq, out var st);
            var pq = Quaternion.Normalize(rot * sq);
            var pt = pos + Vector3.Transform(st, rot);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                mover.BoxOf(i, b, out var bc, out var bh);
                var obb = new Obb(pt + Vector3.Transform(bc, pq), Vector3.Max(bh - new Vector3(0.012f), new Vector3(0.004f)), pq);
                if (q != null && q.OverlapBox(obb, QueryFilter.All))
                    return "it would be inside the ground or a wall";
                for (var k = 0; k < feet.Length; k++)
                    if (Geometry.ObbOverlap(obb, new Obb(feet[k] + new Vector3(0, World.PlayerHeight * 0.5f, 0), new Vector3(World.PlayerRadius, World.PlayerHeight * 0.5f, World.PlayerRadius), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)))
                        return "someone is standing there";
                var reach = obb.Half.Length();
                if (Vector3.Distance(target.WorldCom, obb.Center) > target.Radius + reach)
                    continue;
                for (var j = 0; j < target.PartCount; j++)
                {
                    var od = target.DefOf(j);
                    target.WorldFrame(target.Parts[j].Seg, out var cq, out var ct);
                    for (var k = 0; k < od.SolidBoxes; k++)
                    {
                        target.BoxOf(j, k, out var oc, out var oh);
                        var oc2 = ct + Vector3.Transform(oc, cq);
                        if (Vector3.Distance(oc2, obb.Center) > oh.Length() + reach)
                            continue;
                        if (Geometry.ObbOverlap(obb, new Obb(oc2, oh, cq)))
                            return $"it would overlap the {od.Name}";
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A point on a flat face as a target (sockets guide, they don't limit): the face of part <paramref name="part"/>'s box
    /// under <paramref name="world"/> (the box face whose outward axis is nearest <paramref name="normal"/>), the point on
    /// the 1 cm grid of that segment's rest frame, on the face's plane. False: no solid box there.
    /// </summary>
    public static bool FaceAt(Construct c, int part, Vector3 world, Vector3 normal, out WeldTarget w)
    {
        w = new WeldTarget { Body = c.Id, Part = (byte)part, Socket = FacePoint };
        if (part < 0 || part >= c.PartCount)
            return false;
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        var local = Vector3.Transform(world - t, iq);
        var n = Vector3.Transform(normal, iq);
        var d = c.DefOf(part);
        var best = -1;
        var bestD = float.MaxValue;
        Vector3 bc = default, bh = default;
        for (var b = 0; b < d.SolidBoxes; b++)
        {
            c.BoxOf(part, b, out var oc, out var oh);
            var e = Vector3.Max(Vector3.Abs(local - oc) - oh, Vector3.Zero).Length();
            if (e < bestD)
            {
                bestD = e;
                best = b;
                bc = oc;
                bh = oh;
            }
        }
        if (best < 0 || bestD > 0.05f)
            return false;
        byte dir = 0;
        var most = float.MinValue;
        for (byte k = 0; k < 6; k++)
        {
            var dot = Vector3.Dot(ConstructParts.DirVector(k), n);
            if (dot > most)
            {
                most = dot;
                dir = k;
            }
        }
        var axis = ConstructParts.DirVector(dir);
        var p = new Vector3(MathF.Round(local.X / Grid) * Grid, MathF.Round(local.Y / Grid) * Grid, MathF.Round(local.Z / Grid) * Grid);
        p = Vector3.Clamp(p, bc - bh, bc + bh);
        // On the face's plane exactly (the grid point may sit a hair inside it).
        var plane = Vector3.Dot(bc + axis * Vector3.Dot(bh, Vector3.Abs(axis)), axis);
        p += axis * (plane - Vector3.Dot(p, axis));
        w.At = p;
        w.Dir = dir;
        return true;
    }

    /// <summary>
    /// The socket markers to show: the sockets of the aimed body and of the bodies nearest the crosshair (at most
    /// <see cref="MaxBodies"/>), within <see cref="ShowRange"/>, the <see cref="MaxMarkers"/> nearest the crosshair's ray,
    /// faded with distance. <paramref name="source"/> (its body, part and socket) is marked selected; with
    /// <paramref name="targets"/>, a free socket on a body other than the source's is a target. Returns how many.
    /// </summary>
    public static int Markers(ConstructWorld world, Vector3 eye, Vector3 dir, Construct? aimed, Span<WeldMarker> into,
        ushort sourceBody = 0, int sourcePart = -1, int sourceSocket = -1, bool targets = false)
    {
        Span<ushort> bodies = stackalloc ushort[MaxBodies];
        Span<float> along = stackalloc float[MaxBodies];
        var nb = 0;
        if (aimed != null)
        {
            bodies[nb] = aimed.Id;
            along[nb++] = -1;
        }
        foreach (var c in world.Bodies)
        {
            if (c == aimed || c.PartCount == 0)
                continue;
            var to = c.WorldCom - eye;
            var t = Vector3.Dot(to, dir);
            if (t < 0 || t > ShowRange + c.Radius)
                continue;
            var off = (to - dir * t).Length();
            if (off > c.Radius + 0.4f + t * NearSine)
                continue;
            // Kept nearest first (insertion into the few slots).
            var at = nb;
            while (at > 0 && along[at - 1] > t)
                at--;
            if (at >= MaxBodies)
                continue;
            for (var k = Math.Min(nb, MaxBodies - 1); k > at; k--)
            {
                bodies[k] = bodies[k - 1];
                along[k] = along[k - 1];
            }
            bodies[at] = c.Id;
            along[at] = t;
            if (nb < MaxBodies)
                nb++;
        }
        var n = 0;
        var cap = Math.Min(into.Length, MaxMarkers);
        for (var b = 0; b < nb; b++)
        {
            if (world.Get(bodies[b]) is not { } c)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                if (c.Parts[i].Broken)
                    continue;
                var sockets = c.DefOf(i).Sockets.Length;
                c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                for (var s = 0; s < sockets; s++)
                {
                    c.SocketAt(i, s, out var at, out var sdir);
                    var wAt = t + Vector3.Transform(at, q);
                    var to = wAt - eye;
                    var dist = to.Length();
                    var fade = Math.Clamp((ShowRange - dist) / 3f, 0f, 1f);
                    if (fade <= 0 || Vector3.Dot(to, dir) <= 0.05f)
                        continue;
                    var offRay = (to - dir * Vector3.Dot(to, dir)).Length() / dist;
                    var used = c.SocketUsed(i, s);
                    var state = c.Id == sourceBody && i == sourcePart && s == sourceSocket ? WeldMarkerState.Selected
                        : used ? WeldMarkerState.Used
                        : targets && c.Id != sourceBody ? WeldMarkerState.Target : WeldMarkerState.Free;
                    var m = new WeldMarker { Body = c.Id, Part = (byte)i, Socket = (byte)s, At = wAt, Dir = Vector3.Transform(ConstructParts.DirVector(sdir), q), State = state, Fade = fade, Off = offRay };
                    if (n < cap)
                    {
                        into[n++] = m;
                        continue;
                    }
                    // Full: this one replaces the one furthest off the crosshair, if it is nearer.
                    var worst = 0;
                    for (var k = 1; k < n; k++)
                        if (into[k].Off > into[worst].Off)
                            worst = k;
                    if (into[worst].Off > offRay && into[worst].State != WeldMarkerState.Selected)
                        into[worst] = m;
                }
            }
        }
        return n;
    }

    /// <summary>The marker the crosshair magnet-snaps to: the one nearest the ray within <see cref="PickSine"/> (only on <paramref name="onlyBody"/>, never on <paramref name="notBody"/>, free sockets only), or -1.</summary>
    public static int PickMarker(ReadOnlySpan<WeldMarker> markers, ushort onlyBody = 0, ushort notBody = 0)
    {
        var best = -1;
        var bestOff = PickSine;
        for (var k = 0; k < markers.Length; k++)
        {
            ref readonly var m = ref markers[k];
            if (m.State == WeldMarkerState.Used || onlyBody != 0 && m.Body != onlyBody || notBody != 0 && m.Body == notBody)
                continue;
            if (m.Off < bestOff)
            {
                bestOff = m.Off;
                best = k;
            }
        }
        return best;
    }

    /// <summary>
    /// Smart rotation's start angle (sr01) for a one-part piece onto a real socket: the best-ranked turn that fits, as an
    /// angle; <paramref name="rots"/> gets the ranked part turns (best first) for R and for learning. Other pieces start at 0.
    /// Returns how many turns were ranked.
    /// </summary>
    public static int StartAngle(Construct target, in WeldTarget w, Construct mover, int part, int socket, SmartRotationMemory? memory, Span<int> variants, Span<byte> rots, out float degrees)
    {
        degrees = 0;
        if (w.Face || mover.PartCount != 1 || w.Part >= target.PartCount || !SourceFrame(mover, part, socket, out _, out var sDir) || !TargetFrame(target, w, out _, out var tDir))
            return 0;
        var n = SmartRotation.Rank(target, mover.DefOf(part), w.Part, w.Socket, memory, variants, rots, onlyDefSocket: socket);
        if (n > 0)
            degrees = AngleOfPartTurn(mover, part, sDir, tDir, rots[0]);
        return n;
    }

    /// <summary>The angle that puts part <paramref name="part"/> of the piece at part turn <paramref name="partTurn"/> (the target frame).</summary>
    public static float AngleOfPartTurn(Construct mover, int part, byte sourceDir, byte targetDir, byte partTurn) =>
        AngleOf(sourceDir, targetDir, Quaternion.Normalize(Rot24.Quat(partTurn) * Quaternion.Conjugate(Rot24.Quat(mover.Parts[part].Rot))));

    /// <summary>The part's Rot24 turn in the target frame at a grid angle (smart rotation learns it).</summary>
    public static byte PartTurnAt(Construct mover, int part, Quaternion relRot) =>
        Rot24.Nearest(Quaternion.Normalize(relRot * Rot24.Quat(mover.Parts[part].Rot)), out _);
}

public sealed partial class ConstructWorld
{
    /// <summary>
    /// The socket weld itself (call <see cref="SocketWeld.Check"/> first): the piece goes to the pose and joins the
    /// construct, merged on its Rot24 grid (linked socket to socket) when <see cref="SocketWeld.OnRotGrid"/>, else as a
    /// fixed-pose freeform segment on the target part's segment. The piece goes. Null, or why not.
    /// </summary>
    public string? SocketWeldApply(Construct target, Construct mover, int part, int socket, in WeldTarget w, float degrees)
    {
        if (!SocketWeld.Pose(mover, part, socket, target, w, degrees, out var relPos, out var relRot, out var pos, out var rot))
            return "that socket is gone";
        if (SocketWeld.OnRotGrid(mover, part, socket, target, w, degrees))
        {
            var turn = Rot24.Nearest(relRot, out var match);
            if (match < 0.9999f)
                return "that turn is off the grid";
            var plan = new JoinPlan { Other = mover, PartA = w.Part, SockA = w.Socket, PartB = part, SockB = socket, Turn = turn, Offset = relPos };
            return Merge(target, plan) ? null : "they can't be joined";
        }
        // Off the grid: the piece snaps to the ghost and is welded on there as a fixed-pose segment.
        var vel = target.PointVel(pos + Vector3.Transform(mover.Com, rot));
        mover.Pos = mover.PrevPos = pos;
        mover.Rot = mover.PrevRot = rot;
        mover.Vel = vel;
        mover.AngVel = target.AngVel;
        return FreeformWeld(target, w.Part, mover, part) >= 0 ? null : "they can't be welded";
    }
}
