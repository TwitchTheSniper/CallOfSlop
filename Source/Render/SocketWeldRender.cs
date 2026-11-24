using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's socket weld drawn (board item dev-sandbox-socket-weld-3step-sw01; Program.SandboxSocketWeld.cs sets the
/// state each frame): a dot and a short arrow for each socket marker, coloured by state (free, used, the picked source, a
/// target) and faded with distance; the piece's ghost at the pose, green or red; in step 3 a ring of ticks about the
/// socket axis (every 15 degrees, bigger on 90) with the angle's mark; a thin tether from a piece picked far off to where it
/// goes, and a streak along it when it is pulled (this PC only: the weld itself moves the piece at once). Nothing allocates.
/// </summary>
public static class SocketWeldRender
{
    public static readonly WeldMarker[] Markers = new WeldMarker[SocketWeld.MaxMarkers];
    public static int MarkerCount;
    public static int Hot = -1; // the marker the crosshair snaps to
    public static Construct? Ghost;
    public static Vector3 GhostPos;
    public static Quaternion GhostRot = Quaternion.Identity;
    public static bool Valid, Spinning;
    public static float Angle;
    public static Vector3 AxisAt, Axis, AxisRef;
    public static int MarkersDrawn; // (the last frame)
    public static bool PointOn, TargetOn; // the point weld's clicked origin (orange) and target point (green), with their normals (board item pw01)
    public static Vector3 PointAt, PointDir, TargetAt, TargetDir;
    public const float TetherFrom = 1.5f; // metres: a piece this near the ghost needs no tether
    public const float PullFade = 0.35f; // seconds: how long the streak of a finished pull fades
    public static bool TetherOn;
    public static Vector3 TetherA, TetherB;
    private static Vector3 _pullA, _pullB;
    private static float _pullFor; // seconds to draw the streak: 0 none
    private static double _pullEnd = -1; // on the clock Draw is given, set on its first frame

    /// <summary>The weld seam under the crosshair (Program.SandboxWeldSeam.cs): a flat box in the rest frame of its body's segment, drawn on the body as it is drawn; <see cref="SeamOk"/> false: it can't be broken (grey).</summary>
    public static Construct? SeamBody;
    public static int SeamSeg;
    public static Vector3 SeamMin, SeamMax;
    public static bool SeamOk;

    /// <summary>
    /// The bodies the overlay was worked out on, with their poses then. The input runs before the frame's ticks, so a body
    /// moving fast (a ship in orbit) is drawn a frame's travel on from where its markers were found: each point is carried
    /// with its body to where the body is drawn (<see cref="Game.DrawnPose"/>). <see cref="OnBody"/> holds the ghost, the
    /// axis ring, the point weld's target and the tether's end; <see cref="FromBody"/> the point weld's origin and the
    /// tether's start; a marker its own body.
    /// </summary>
    private struct Stamp
    {
        public ushort Body;
        public Vector3 Pos, To;
        public Quaternion Rot, Turn;
    }

    private static readonly Stamp[] _stamps = new Stamp[SocketWeld.MaxBodies + 4];
    private static int _stampCount;
    private static Stamp _pullOn; // (the streak's body: it lasts a timed weld's seconds)
    public static ushort OnBody, FromBody;

    /// <summary>The input: <paramref name="c"/>'s pose now, which the points just worked out on it were taken in.</summary>
    public static void Follow(Construct? c)
    {
        if (c == null)
            return;
        var k = 0;
        while (k < _stampCount && _stamps[k].Body != c.Id)
            k++;
        if (k == _stamps.Length)
            return; // (full: drawn where it was found)
        if (k == _stampCount)
            _stampCount++;
        _stamps[k] = new Stamp { Body = c.Id, Pos = c.Pos, Rot = c.Rot };
    }

    /// <summary>The input: every marker's body stamped.</summary>
    public static void FollowMarkers(ConstructWorld world)
    {
        ushort last = 0;
        for (var k = 0; k < MarkerCount; k++)
            if (Markers[k].Body != last)
                Follow(world.Get(last = Markers[k].Body));
    }

    private static void Drawn(Game game, ref Stamp s)
    {
        if (s.Body != 0 && game.Constructs.Get(s.Body) is { } c)
        {
            game.DrawnPose(c, out s.To, out var rot);
            s.Turn = Quaternion.Normalize(rot * Quaternion.Conjugate(s.Rot));
        }
        else
            (s.To, s.Turn) = (s.Pos, Quaternion.Identity);
    }

    private static int StampOf(ushort body)
    {
        for (var k = 0; k < _stampCount; k++)
            if (_stamps[k].Body == body)
                return k;
        return -1;
    }

    private static Vector3 At(int k, Vector3 p) => k < 0 ? p : _stamps[k].To + Vector3.Transform(p - _stamps[k].Pos, _stamps[k].Turn);
    private static Vector3 Dir(int k, Vector3 d) => k < 0 ? d : Vector3.Transform(d, _stamps[k].Turn);

    public static void Clear()
    {
        _stampCount = 0;
        OnBody = FromBody = 0;
        SeamBody = null;
        MarkerCount = 0;
        PointOn = TargetOn = TetherOn = false;
        Hot = -1;
        Ghost = null;
        Spinning = false;
    }

    /// <summary>The weld was sent: a streak from the piece to where it goes, for <paramref name="hold"/> seconds (a timed weld's time) and the fade.</summary>
    public static void Pull(Vector3 from, Vector3 to, float hold)
    {
        var k = StampOf(OnBody);
        _pullOn = k >= 0 ? _stamps[k] : default;
        _pullA = from;
        _pullB = to;
        _pullFor = hold + PullFade;
        _pullEnd = -1;
    }

    /// <summary>A timed weld was let go: no pull after all.</summary>
    public static void PullStop() => _pullFor = 0;

    public static Vector3 Colour(WeldMarkerState s) => s switch
    {
        WeldMarkerState.Used => new Vector3(0.55f, 0.55f, 0.6f),
        WeldMarkerState.Selected => new Vector3(1f, 0.75f, 0.2f),
        WeldMarkerState.Target => new Vector3(0.3f, 1f, 0.45f),
        _ => new Vector3(0.35f, 0.75f, 1f),
    };

    public static void Draw(Renderer gfx, Game game, double now)
    {
        MarkersDrawn = 0;
        for (var k = 0; k < _stampCount; k++)
            Drawn(game, ref _stamps[k]);
        var on = StampOf(OnBody);
        var from = StampOf(FromBody);
        var pulse = 0.55f + 0.25f * MathF.Sin((float)now * 6f);
        if (SeamBody is { } sb)
        {
            gfx.SetDepthTest(false); // (seen through the parts, faint: a seam is often behind the part aimed at)
            try
            {
                DrawSeam(gfx, sb, pulse, 0.4f);
            }
            finally
            {
                gfx.SetDepthTest(true);
            }
            DrawSeam(gfx, sb, pulse, 1f);
        }
        for (var k = 0; k < MarkerCount; k++)
        {
            ref var m = ref Markers[k];
            var mk = StampOf(m.Body);
            var mAt = At(mk, m.At);
            var mDir = Dir(mk, m.Dir);
            var hot = k == Hot;
            var sel = m.State == WeldMarkerState.Selected;
            var used = m.State == WeldMarkerState.Used;
            // The shape and colour say what the socket connects (Sean 2026-11-24); the state is the size, a white core when
            // the crosshair is on it, a pulsing orange shell on the picked source, and grey and faint when in use.
            var col = m.Side != 0 ? (m.Side == JointSides.Leaf ? JointSides.LeafColour : JointSides.BaseColour) : SocketWeld.LookColour(m.Look);
            if (used)
                col = Vector3.Lerp(col, Colour(WeldMarkerState.Used), 0.7f);
            var a = m.Fade * (hot || sel ? 0.95f : used ? 0.35f : 0.7f);
            var cls = 1f + 0.25f * MathF.Min(m.Class, (byte)3);
            var size = (hot ? 0.065f : used ? 0.03f : 0.045f) * m.Size * cls;
            var at = mAt + mDir * 0.02f;
            Glyph(gfx, m.Look, m.Side, at, mDir, col, a, size);
            if (hot)
                gfx.Draw(Matrix4x4.CreateScale(size * 0.9f) * Matrix4x4.CreateTranslation(at + mDir * size * 0.6f), new Vector3(1f, 1f, 1f), 0.95f * m.Fade, mode: 2);
            if (sel)
                gfx.Draw(Matrix4x4.CreateScale(size * 4.2f) * Matrix4x4.CreateTranslation(at), Colour(WeldMarkerState.Selected), 0.3f * pulse * m.Fade, mode: 2);
            if (!used)
                Bar(gfx, at, at + mDir * (hot ? 0.16f : 0.11f) * m.Size * cls, size * 0.3f, col, a);
            MarkersDrawn++;
        }
        if (PointOn)
            PointMarker(gfx, At(from, PointAt), Dir(from, PointDir), new Vector3(1f, 0.6f, 0.15f));
        if (TargetOn)
            PointMarker(gfx, At(on, TargetAt), Dir(on, TargetDir), new Vector3(0.3f, 1f, 0.45f));
        if (_pullFor > 0)
        {
            if (_pullEnd < 0)
                _pullEnd = now + _pullFor;
            var left = (float)(_pullEnd - now);
            if (left <= 0)
                _pullFor = 0;
            else
            {
                Drawn(game, ref _pullOn);
                Bar(gfx, PullAt(_pullA), PullAt(_pullB), 0.03f, new Vector3(0.45f, 0.75f, 1f), 0.8f * MathF.Min(1f, left / PullFade));
            }
        }
        if (Ghost is not { } c)
            return;
        var ghostRot = on < 0 ? GhostRot : Quaternion.Normalize(_stamps[on].Turn * GhostRot);
        var body = Matrix4x4.CreateFromQuaternion(ghostRot) * Matrix4x4.CreateTranslation(At(on, GhostPos));
        var tint = Valid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
        if (TetherOn)
            Bar(gfx, At(from, TetherA), At(on, TetherB), 0.012f, tint, 0.45f);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            c.SegPose(p.Seg, out var q, out var t);
            var seg = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * body;
            for (var b = 0; b < d.Boxes.Length; b++)
            {
                ref readonly var box = ref d.Boxes[b];
                if (box.Hidden)
                    continue;
                var bc = p.Pos + Rot24.Apply(p.Rot, box.Center);
                gfx.Draw(Matrix4x4.CreateScale(1.01f) * box.Model(p.Rot, bc) * seg, tint, 0.35f * pulse * ConstructRender.GhostGlass(box), mode: 2, shape: box.Shape);
            }
        }
        if (!Spinning || Axis.LengthSquared() < 0.5f)
            return;
        // The ring of ticks about the socket axis, and the angle's mark.
        var axisAt = At(on, AxisAt);
        var axisRef = Dir(on, AxisRef);
        var y = Vector3.Cross(Dir(on, Axis), axisRef);
        const float r = 0.32f;
        for (var k = 0; k < 24; k++)
        {
            var th = k * MathF.PI / 12f;
            var dirK = axisRef * MathF.Cos(th) + y * MathF.Sin(th);
            var big = k % 6 == 0;
            Bar(gfx, axisAt + dirK * r, axisAt + dirK * (r + (big ? 0.07f : 0.035f)), big ? 0.012f : 0.007f, new Vector3(0.9f, 0.9f, 0.95f), big ? 0.85f : 0.5f);
        }
        var ta = Angle * MathF.PI / 180f;
        var mark = axisRef * MathF.Cos(ta) + y * MathF.Sin(ta);
        Bar(gfx, axisAt, axisAt + mark * (r + 0.1f), 0.014f, tint, 0.9f);
    }

    /// <summary>
    /// One socket marker's shape, flat across the socket's facing: snappy building's for the connection types (cube: a mount,
    /// a bar with a tip: a shaft, diamond: a gun part, cross: fuel, power or data, disc: a hub or a leaf; a joint's sides their
    /// own), and for the special kinds: an open ring (an extruder die), a square frame (a press die), a pair of rails (a
    /// run-out), a frame with a bar across (a module), a stack of three plates (a tier slot) and a small dot (a shaft port).
    /// </summary>
    private static void Glyph(Renderer gfx, WeldLook look, byte side, Vector3 at, Vector3 dir, Vector3 col, float a, float size)
    {
        if (side != 0 || (byte)look < (byte)WeldLook.Die)
        {
            SnapRender.Marker(gfx, (ConnType)Math.Min((byte)look, (byte)ConnType.EngineStack), at, dir, col, a, size, side);
            return;
        }
        var z = dir.LengthSquared() < 1e-6f ? Vector3.UnitY : Vector3.Normalize(dir);
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var t = size * 0.4f;
        switch (look)
        {
            case WeldLook.Die:
                for (var k = 0; k < 6; k++)
                {
                    var a0 = k * MathF.PI / 3f;
                    var a1 = a0 + MathF.PI / 3f;
                    Bar(gfx, at + (x * MathF.Cos(a0) + y * MathF.Sin(a0)) * size * 1.2f, at + (x * MathF.Cos(a1) + y * MathF.Sin(a1)) * size * 1.2f, t, col, a);
                }
                break;
            case WeldLook.PressDie or WeldLook.Module:
                for (var k = 0; k < 4; k++)
                {
                    var u = (k & 1) == 0 ? x : y;
                    var v = (k & 1) == 0 ? y : x;
                    var o = (k < 2 ? 1f : -1f) * size * 1.1f;
                    Bar(gfx, at + u * o - v * size * 1.1f, at + u * o + v * size * 1.1f, t, col, a);
                }
                if (look == WeldLook.Module)
                    Bar(gfx, at - x * size * 1.1f, at + x * size * 1.1f, t, col, a);
                break;
            case WeldLook.RunOut:
                Bar(gfx, at - x * size * 0.6f - y * size * 1.3f, at - x * size * 0.6f + y * size * 1.3f, t, col, a);
                Bar(gfx, at + x * size * 0.6f - y * size * 1.3f, at + x * size * 0.6f + y * size * 1.3f, t, col, a);
                break;
            case WeldLook.Tier:
                for (var k = 0; k < 3; k++)
                    Bar(gfx, at + z * size * 0.55f * k - x * size, at + z * size * 0.55f * k + x * size, size * 0.3f, col, a);
                break;
            default:
                gfx.Draw(Matrix4x4.CreateScale(size * 0.6f) * Matrix4x4.CreateTranslation(at), col, a, mode: 2);
                break;
        }
    }

    private static Vector3 PullAt(Vector3 p) => _pullOn.Body == 0 ? p : _pullOn.To + Vector3.Transform(p - _pullOn.Pos, _pullOn.Turn);

    /// <summary>A point weld's point: a dot on the face, a short arrow along its normal and a small cross on the face.</summary>
    private static void PointMarker(Renderer gfx, Vector3 at, Vector3 n, Vector3 col)
    {
        if (n.LengthSquared() < 0.5f)
            return;
        var p = at + n * 0.01f;
        gfx.Draw(Matrix4x4.CreateScale(0.05f) * Matrix4x4.CreateTranslation(p), col, 0.95f, mode: 2);
        Bar(gfx, p, p + n * 0.18f, 0.016f, col, 0.9f);
        var x = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n)) : Vector3.UnitX;
        var y = Vector3.Cross(n, x);
        Bar(gfx, p - x * 0.09f, p + x * 0.09f, 0.008f, col, 0.8f);
        Bar(gfx, p - y * 0.09f, p + y * 0.09f, 0.008f, col, 0.8f);
    }

    /// <summary>The seam that RMB breaks: its outline and a lit strip over it, hot red-orange, or grey when it can't be broken.</summary>
    private static void DrawSeam(Renderer gfx, Construct c, float pulse, float fade)
    {
        if (SeamSeg < 0 || SeamSeg > c.JointCount)
            return;
        c.WorldFrame(SeamSeg, out var q, out var t);
        var col = SeamOk ? new Vector3(1f, 0.3f, 0.1f) : new Vector3(0.55f, 0.55f, 0.6f);
        var centre = t + Vector3.Transform((SeamMin + SeamMax) * 0.5f, q);
        var size = SeamMax - SeamMin;
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(centre), col, (SeamOk ? 0.25f + 0.3f * pulse : 0.3f) * fade, mode: 2);
        // The edges along the face (the thin axis's edges are skipped: millimetres).
        var thick = SeamOk ? 0.018f : 0.012f;
        for (var e = 0; e < 12; e++)
        {
            var axis = e / 4;
            var bits = e % 4;
            if ((axis == 0 ? size.X : axis == 1 ? size.Y : size.Z) < 0.02f)
                continue;
            Vector3 a = SeamMin, b = SeamMax;
            var u = (bits & 1) != 0;
            var v = (bits & 2) != 0;
            switch (axis)
            {
                case 0:
                    a = new Vector3(SeamMin.X, u ? SeamMax.Y : SeamMin.Y, v ? SeamMax.Z : SeamMin.Z);
                    b = a with { X = SeamMax.X };
                    break;
                case 1:
                    a = new Vector3(u ? SeamMax.X : SeamMin.X, SeamMin.Y, v ? SeamMax.Z : SeamMin.Z);
                    b = a with { Y = SeamMax.Y };
                    break;
                default:
                    a = new Vector3(u ? SeamMax.X : SeamMin.X, v ? SeamMax.Y : SeamMin.Y, SeamMin.Z);
                    b = a with { Z = SeamMax.Z };
                    break;
            }
            Bar(gfx, t + Vector3.Transform(a, q), t + Vector3.Transform(b, q), thick, col, 0.95f * fade);
        }
        DrawSeamStitches(gfx, q, t, size, col, fade);
    }

    /// <summary>Spacing of the stitch ticks along a seam's edge (m), and the fewest and most on one edge.</summary>
    private const float StitchSpacing = 0.2f;
    private const int StitchMin = 2, StitchMax = 16;

    /// <summary>The stitches that tell a seam to break from the attach ghost (user, 2026-11-19): short ticks across the
    /// seam's plane, evenly spaced round its outline and standing just proud of the parts' sides, like a perforated line.</summary>
    private static void DrawSeamStitches(Renderer gfx, Quaternion q, Vector3 t, Vector3 size, Vector3 col, float fade)
    {
        var n = size.X <= size.Y && size.X <= size.Z ? 0 : size.Y <= size.Z ? 1 : 2; // (the slab's thin axis: the seam's normal)
        var thick = SeamOk ? 0.014f : 0.01f;
        var reach = 0.07f; // (each side of the seam's plane)
        var off = thick * 0.5f + 0.004f; // (clear of the parts' side faces: no z-fighting)
        var tick = Vector3.Lerp(col, Vector3.One, 0.35f);
        var mid = (SeamMin + SeamMax) * 0.5f;
        for (var e = 0; e < 3; e++)
        {
            if (e == n)
                continue;
            var o = 3 - n - e; // (the other in-plane axis: the edge's outward side)
            var len = size[e];
            if (len < 0.02f)
                continue;
            var count = Math.Clamp((int)MathF.Round(len / StitchSpacing), StitchMin, StitchMax);
            for (var side = 0; side < 2; side++)
            {
                var p = mid;
                p[o] = side == 0 ? SeamMin[o] - off : SeamMax[o] + off;
                for (var i = 0; i < count; i++)
                {
                    p[e] = SeamMin[e] + len * (i + 0.5f) / count;
                    var a = p;
                    var b = p;
                    a[n] -= reach;
                    b[n] += reach;
                    Bar(gfx, t + Vector3.Transform(a, q), t + Vector3.Transform(b, q), thick, tick, 0.95f * fade);
                }
            }
        }
    }

    /// <summary>A thin box from <paramref name="a"/> to <paramref name="b"/>.</summary>
    internal static void Bar(Renderer gfx, Vector3 a, Vector3 b, float thick, Vector3 colour, float alpha)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-4f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, alpha, mode: 2);
    }
}
