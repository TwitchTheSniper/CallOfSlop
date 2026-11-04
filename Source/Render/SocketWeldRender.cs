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

    public static void Clear()
    {
        MarkerCount = 0;
        PointOn = TargetOn = TetherOn = false;
        Hot = -1;
        Ghost = null;
        Spinning = false;
    }

    /// <summary>The weld was sent: a streak from the piece to where it goes, for <paramref name="hold"/> seconds (a timed weld's time) and the fade.</summary>
    public static void Pull(Vector3 from, Vector3 to, float hold)
    {
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

    public static void Draw(Renderer gfx, double now)
    {
        MarkersDrawn = 0;
        var pulse = 0.55f + 0.25f * MathF.Sin((float)now * 6f);
        for (var k = 0; k < MarkerCount; k++)
        {
            ref var m = ref Markers[k];
            var hot = k == Hot;
            var col = hot && m.State != WeldMarkerState.Selected ? new Vector3(1f, 1f, 1f) : Colour(m.State);
            var a = m.Fade * (hot || m.State == WeldMarkerState.Selected ? 0.95f : 0.6f);
            var size = (hot ? 0.07f : 0.045f) * m.Size;
            var at = m.At + m.Dir * 0.02f;
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at), col, a, mode: 2);
            Bar(gfx, at, at + m.Dir * (hot ? 0.16f : 0.11f) * m.Size, size * 0.35f, col, a);
            MarkersDrawn++;
        }
        if (PointOn)
            PointMarker(gfx, PointAt, PointDir, new Vector3(1f, 0.6f, 0.15f));
        if (TargetOn)
            PointMarker(gfx, TargetAt, TargetDir, new Vector3(0.3f, 1f, 0.45f));
        if (_pullFor > 0)
        {
            if (_pullEnd < 0)
                _pullEnd = now + _pullFor;
            var left = (float)(_pullEnd - now);
            if (left <= 0)
                _pullFor = 0;
            else
                Bar(gfx, _pullA, _pullB, 0.03f, new Vector3(0.45f, 0.75f, 1f), 0.8f * MathF.Min(1f, left / PullFade));
        }
        if (Ghost is not { } c)
            return;
        var body = Matrix4x4.CreateFromQuaternion(GhostRot) * Matrix4x4.CreateTranslation(GhostPos);
        var tint = Valid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
        if (TetherOn)
            Bar(gfx, TetherA, TetherB, 0.012f, tint, 0.45f);
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
        var y = Vector3.Cross(Axis, AxisRef);
        const float r = 0.32f;
        for (var k = 0; k < 24; k++)
        {
            var th = k * MathF.PI / 12f;
            var dirK = AxisRef * MathF.Cos(th) + y * MathF.Sin(th);
            var big = k % 6 == 0;
            Bar(gfx, AxisAt + dirK * r, AxisAt + dirK * (r + (big ? 0.07f : 0.035f)), big ? 0.012f : 0.007f, new Vector3(0.9f, 0.9f, 0.95f), big ? 0.85f : 0.5f);
        }
        var ta = Angle * MathF.PI / 180f;
        var mark = AxisRef * MathF.Cos(ta) + y * MathF.Sin(ta);
        Bar(gfx, AxisAt, AxisAt + mark * (r + 0.1f), 0.014f, tint, 0.9f);
    }

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

    /// <summary>A thin box from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Bar(Renderer gfx, Vector3 a, Vector3 b, float thick, Vector3 colour, float alpha)
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
