using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the routed links (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): every segment of every link is one
/// segment piece of its type stretched between its two points, and every bend one elbow piece, all instanced (one batch:
/// <see cref="Renderer.BeginBatch"/>). Data: a small flat cable; power: a larger flat cable with a stripe; pipes: a
/// round pipe (two boxes turned 45 degrees about it) in one neutral look for every fluid, with a flange ring at each end of
/// every section (so at its nodes); shafts: a round steel shaft
/// with a U joint at each bend; chutes: a square duct with rims. A cut segment shows two broken stubs (a power cable
/// sparks). Links are always drawn; data and power are dimmer unless the linking tool
/// is out. The linking tool's preview (the link being routed, green or red) and "highlight all" draw on top of the parts.
/// </summary>
public static class RouteRender
{
    public static int LastSegments { get; private set; } // (the self-test)

    // The linking tool's preview (Program.SandboxLinks.cs fills it): rest-frame points and their segments on PreviewBody.
    public static Construct? PreviewBody;
    public static readonly Vector3[] PreviewPts = new Vector3[Construct.MaxNodesPerRoute + 3];
    public static readonly byte[] PreviewSeg = new byte[Construct.MaxNodesPerRoute + 3];
    public static int PreviewCount;
    public static bool PreviewOk;
    public static RouteType PreviewType;
    public static bool Highlight; // every link bright and through the parts (the edit ring's "highlight all")
    public static Construct? AimBody; // the link under the crosshair (outlined)
    public static int AimRoute = -1;

    private static readonly Matrix4x4[] SegFrames = new Matrix4x4[Construct.MaxJoints + 1];

    public static void ClearPreview()
    {
        PreviewBody = null;
        PreviewCount = 0;
        AimBody = null;
        AimRoute = -1;
    }

    /// <summary>Every link of every construct (lit, instanced).</summary>
    public static void Draw(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        gfx.BeginBatch();
        foreach (var c in world.Bodies)
        {
            if (c.RouteCount == 0)
                continue;
            Frames(c, alpha, blendTicks);
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref var r = ref c.Routes[k];
                var radius = ConstructRoutes.Radius(r.Type);
                var a = At(c, k, -1);
                var dirPrev = Vector3.Zero;
                for (var i = 0; i <= r.Count; i++)
                {
                    var b = At(c, k, i);
                    var d = b - a;
                    var len = d.Length();
                    if (len > 1e-4f)
                    {
                        var z = d / len;
                        var cut = ConstructRoutes.SegmentHealth(c, k, i) <= 0;
                        if (cut)
                        {
                            // Two stubs with a gap between them.
                            var gap = MathF.Min(0.12f, len * 0.3f);
                            var half = (len - gap) * 0.5f;
                            Piece(gfx, r.Type, a, a + z * half, z, radius, dim: true, paint: r.Paint);
                            Piece(gfx, r.Type, b - z * half, b, z, radius, dim: true, paint: r.Paint);
                            if (r.Type == RouteType.Power && c.Charge > 0 && ((int)(ConstructRender.Now * 23) + k) % 3 == 0)
                                gfx.Draw(Matrix4x4.CreateScale(0.04f) * Matrix4x4.CreateTranslation(a + z * (half + gap * 0.5f)), new Vector3(1f, 0.9f, 0.5f), 1f, mode: 2); // (sparks)
                        }
                        else
                            Piece(gfx, r.Type, a, b, z, radius, dim: r.Type is RouteType.Data or RouteType.Power && !ConstructRender.ShowWires, paint: r.Paint);
                        n++;
                        if (dirPrev != Vector3.Zero && Vector3.Dot(dirPrev, z) < 0.9998f)
                            Elbow(gfx, r.Type, a, dirPrev, z, radius, r.Paint);
                        dirPrev = z;
                    }
                    a = b;
                }
            }
        }
        gfx.EndBatch();
        LastSegments = n;
    }

    /// <summary>The preview of the link being routed, highlight-all and the aimed link: unlit, through the parts (only with the linking tool).</summary>
    public static void DrawOverlay(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        if (!ConstructRender.ShowWires)
            return;
        gfx.SetDepthTest(false);
        foreach (var c in world.Bodies)
        {
            if (c.RouteCount == 0 || !Highlight && AimBody != c)
                continue;
            Frames(c, alpha, blendTicks);
            for (var k = 0; k < c.RouteCount; k++)
            {
                if (!Highlight && k != AimRoute)
                    continue;
                ref var r = ref c.Routes[k];
                var colour = k == AimRoute && AimBody == c ? new Vector3(1f, 1f, 0.4f) : Colour(r.Type) * 1.4f;
                var a = At(c, k, -1);
                for (var i = 0; i <= r.Count; i++)
                {
                    var b = At(c, k, i);
                    Line(gfx, a, b, ConstructRoutes.SegmentHealth(c, k, i) <= 0 ? new Vector3(1f, 0.25f, 0.2f) : colour, 0.8f, ConstructRoutes.Radius(r.Type) * 0.8f);
                    a = b;
                }
            }
        }
        if (PreviewBody is { } pc && PreviewCount >= 2 && world.Get(pc.Id) == pc)
        {
            Frames(pc, alpha, blendTicks);
            var colour = PreviewOk ? new Vector3(0.35f, 1f, 0.45f) : new Vector3(1f, 0.3f, 0.25f);
            var radius = ConstructRoutes.Radius(PreviewType);
            for (var i = 1; i < PreviewCount; i++)
            {
                var a = Vector3.Transform(PreviewPts[i - 1], SegFrames[Math.Min(PreviewSeg[i - 1], (byte)Construct.MaxJoints)]);
                var b = Vector3.Transform(PreviewPts[i], SegFrames[Math.Min(PreviewSeg[i], (byte)Construct.MaxJoints)]);
                Line(gfx, a, b, i == PreviewCount - 1 ? colour : new Vector3(0.9f, 0.9f, 0.4f), 0.75f, radius);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f) * Matrix4x4.CreateTranslation(b), i == PreviewCount - 1 ? colour : new Vector3(1f, 0.85f, 0.3f), 0.85f, mode: 2);
            }
        }
        gfx.SetDepthTest(true);
    }

    /// <summary>Each segment's rest frame to world matrix this frame (blended between ticks).</summary>
    private static void Frames(Construct c, float alpha, bool blendTicks)
    {
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        SegFrames[0] = body;
        for (var s = 1; s <= c.JointCount && s <= Construct.MaxJoints; s++)
        {
            ConstructRender.SegPoseBlended(c, s, blendTicks ? alpha : 1f, out var q, out var t);
            SegFrames[s] = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * body;
        }
    }

    private static Vector3 At(Construct c, int route, int i)
    {
        var p = ConstructRoutes.Point(c, route, i, out var seg);
        return Vector3.Transform(p, SegFrames[seg >= 0 && seg <= c.JointCount && seg <= Construct.MaxJoints ? seg : 0]);
    }

    public static Vector3 Colour(RouteType t) => t switch
    {
        RouteType.Data => new Vector3(0.18f, 0.42f, 0.26f),
        RouteType.Power => new Vector3(0.12f, 0.12f, 0.13f),
        RouteType.Pipe => new Vector3(0.62f, 0.64f, 0.68f), // one neutral pipe for every fluid (decision 28)
        RouteType.Shaft => new Vector3(0.5f, 0.52f, 0.55f),
        _ => new Vector3(0.45f, 0.47f, 0.42f),
    };

    internal static Matrix4x4 Basis(Vector3 z, float roll = 0)
    {
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        if (roll != 0)
        {
            var cr = MathF.Cos(roll);
            var sr = MathF.Sin(roll);
            (x, y) = (x * cr + y * sr, y * cr - x * sr);
        }
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>One segment piece of a type from <paramref name="a"/> to <paramref name="b"/>.</summary>
    internal static void Piece(Renderer gfx, RouteType type, Vector3 a, Vector3 b, Vector3 z, float radius, bool dim, uint paint = 0)
    {
        var len = Vector3.Distance(a, b);
        if (len < 1e-4f)
            return;
        var mid = (a + b) * 0.5f;
        var colour = (paint != 0 ? ConstructPaint.ToVector(paint) : Colour(type)) * (dim ? 0.55f : 1f);
        var basis = Basis(z);
        var at = Matrix4x4.CreateTranslation(mid);
        switch (type)
        {
            case RouteType.Data:
                gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 1.25f, len) * basis * at, colour);
                break;
            case RouteType.Power:
                gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 1.3f, len) * basis * at, colour);
                gfx.Draw(Matrix4x4.CreateScale(radius * 0.5f, radius * 1.34f, len * 0.999f) * basis * at, new Vector3(0.75f, 0.12f, 0.1f) * (dim ? 0.55f : 1f)); // (the red stripe)
                break;
            case RouteType.Shaft:
            case RouteType.Pipe:
            {
                // Round: two square boxes turned 45 degrees about the axis make an octagon.
                var d = radius * 1.84f;
                gfx.Draw(Matrix4x4.CreateScale(d, d, len) * basis * at, colour);
                gfx.Draw(Matrix4x4.CreateScale(d, d, len * 0.998f) * Basis(z, MathF.PI / 4) * at, colour);
                if (type == RouteType.Shaft)
                    break;
                // A flange ring at each end, so a pipe reads as round sections joined at its nodes.
                var ring = colour * 0.75f;
                var fd = d * 1.3f;
                var thick = MathF.Min(0.05f, len * 0.4f);
                gfx.Draw(Matrix4x4.CreateScale(fd, fd, thick) * basis * Matrix4x4.CreateTranslation(a + z * (thick * 0.5f)), ring);
                gfx.Draw(Matrix4x4.CreateScale(fd, fd, thick) * basis * Matrix4x4.CreateTranslation(b - z * (thick * 0.5f)), ring);
                break;
            }
            default: // the chute: a square duct with a rim at each end
                gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 2f, len) * basis * at, colour);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.2f, radius * 2.2f, 0.04f) * basis * Matrix4x4.CreateTranslation(a + z * 0.02f), colour * 0.7f);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.2f, radius * 2.2f, 0.04f) * basis * Matrix4x4.CreateTranslation(b - z * 0.02f), colour * 0.7f);
                break;
        }
    }

    /// <summary>The elbow piece at a bend: a joint block (a shaft's U joint: a cross between two yokes).</summary>
    internal static void Elbow(Renderer gfx, RouteType type, Vector3 at, Vector3 from, Vector3 to, float radius, uint paint = 0)
    {
        var colour = paint != 0 ? ConstructPaint.ToVector(paint) : Colour(type);
        if (type == RouteType.Shaft)
        {
            var steel = new Vector3(0.32f, 0.33f, 0.35f);
            gfx.Draw(Matrix4x4.CreateScale(radius * 3.2f, radius * 0.9f, radius * 0.9f) * Basis(from) * Matrix4x4.CreateTranslation(at), steel);
            gfx.Draw(Matrix4x4.CreateScale(radius * 0.9f, radius * 3.2f, radius * 0.9f) * Basis(to) * Matrix4x4.CreateTranslation(at), steel);
            gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f, radius * 2.4f, radius * 1.2f) * Basis(from) * Matrix4x4.CreateTranslation(at - from * radius * 1.2f), colour * 0.8f);
            gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f, radius * 2.4f, radius * 1.2f) * Basis(to) * Matrix4x4.CreateTranslation(at + to * radius * 1.2f), colour * 0.8f);
            return;
        }
        var mid = Vector3.Normalize(from + to);
        var size = type == RouteType.Chute ? radius * 2.1f : radius * 2.05f;
        gfx.Draw(Matrix4x4.CreateScale(size) * Basis(mid.LengthSquared() > 1e-6f ? mid : from) * Matrix4x4.CreateTranslation(at), colour * 0.9f);
    }

    internal static void Line(Renderer gfx, Vector3 a, Vector3 b, Vector3 colour, float opacity, float radius)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        gfx.Draw(Matrix4x4.CreateScale(MathF.Max(0.03f, radius * 2f), MathF.Max(0.03f, radius * 2f), len) * Basis(d / len) * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, opacity, mode: 2);
    }
}
