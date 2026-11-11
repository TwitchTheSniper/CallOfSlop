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
    public static int LastBellows { get; private set; } // (flexible sections drawn; the self-test)

    // The linking tool's preview (Program.SandboxLinks.cs fills it): rest-frame points and their segments on PreviewBody.
    public static Construct? PreviewBody;
    public static readonly Vector3[] PreviewPts = new Vector3[Construct.MaxNodesPerRoute + 3];
    public static readonly byte[] PreviewSeg = new byte[Construct.MaxNodesPerRoute + 3];
    public static int PreviewCount;
    public const int PreviewTail = 128; // the overlay shows this many of the last segments
    public static bool PreviewOk;
    public static RouteType PreviewType;
    public static int PreviewFrom; // the segments from this one on show the state too (0: only the last): an auto-route's whole run
    public static bool PreviewBusy; // an auto-route still being searched for: neither green nor red yet
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
        LastBellows = 0;
        gfx.BeginBatch();
        foreach (var c in world.Bodies)
        {
            if (c.RouteCount == 0 && c.PieceRackCount == 0)
                continue;
            Frames(c, alpha, blendTicks);
            if (c.PieceRackCount > 0)
                DrawRacks(gfx, world, c);
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
                        if (r.Type == RouteType.Conveyor)
                        {
                            Conveyor(gfx, a, b, z, len, r.ConvW / 100f, cut, r.Paint);
                            n++;
                            if (dirPrev != Vector3.Zero && Vector3.Dot(dirPrev, z) < 0.9998f)
                                ConveyorElbow(gfx, a, dirPrev, z, r.ConvW / 100f, r.Paint);
                            dirPrev = z;
                            a = b;
                            continue;
                        }
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
                        else if (c.FlexSectionCount > 0 && ConstructRoutes.FlexSectionAt(c, k, i) is var flex and >= 0)
                            Bellows(gfx, c, flex, a, b, radius, r.Paint);
                        else
                            Piece(gfx, r.Type, a, b, z, radius, dim: r.Type is RouteType.Data or RouteType.Power && !ConstructRender.ShowWires, paint: r.Paint);
                        if (i == r.Count && !cut && ConstructRoutes.IsHalfShaft(c, r))
                            Plunge(gfx, a, b, z, len, radius);
                        n++;
                        if (dirPrev != Vector3.Zero && Vector3.Dot(dirPrev, z) < 0.9998f)
                            Elbow(gfx, r.Type, a, dirPrev, z, radius, r.Paint);
                        dirPrev = z;
                    }
                    a = b;
                }
            }
            if (c.ConveyedCount > 0)
                DrawRiders(gfx, c);
        }
        gfx.EndBatch();
        LastSegments = n;
    }

    // ---- Conveyors (ConstructConveyor.cs): a roller bed the link's set width, and the pieces riding it -----------------

    private static readonly Vector3 ConvRail = new(0.21f, 0.33f, 0.42f), ConvRoller = new(0.66f, 0.68f, 0.7f), ConvPlate = new(0.17f, 0.18f, 0.19f), ConvMark = new(0.95f, 0.66f, 0.1f);
    private const float RollerPitch = 0.3f, MarkPitch = 1.5f;

    /// <summary>
    /// One conveyor segment from <paramref name="a"/> to <paramref name="b"/> (the line is the rollers' top less 5 mm): a dark
    /// under-plate, a side rail each side in the paint (steel blue by default), steel rollers across between them every
    /// 0.3 m and amber chevrons on the rails' tops pointing the way the pieces go. A cut segment draws two dim stubs.
    /// </summary>
    private static void Conveyor(Renderer gfx, Vector3 a, Vector3 b, Vector3 z, float len, float width, bool cut, uint paint)
    {
        var basis = Basis(z);
        var x = new Vector3(basis.M11, basis.M12, basis.M13);
        var y = new Vector3(basis.M21, basis.M22, basis.M23);
        var dim = cut ? 0.5f : 1f;
        var rail = (paint != 0 ? ConstructPaint.ToVector(paint) : ConvRail) * dim;
        if (cut)
        {
            var gap = MathF.Min(0.3f, len * 0.3f);
            var half = (len - gap) * 0.5f;
            Bed(gfx, basis, x, y, a + z * (half * 0.5f), half, width, rail, dim);
            Bed(gfx, basis, x, y, b - z * (half * 0.5f), half, width, rail, dim);
            return;
        }
        Bed(gfx, basis, x, y, (a + b) * 0.5f, len, width, rail, dim);
        // Rollers across, from half a pitch in (they don't meet the next segment's at a node).
        var roller = Matrix4x4.CreateScale(width - 0.01f, 0.05f, 0.05f) * basis;
        for (var s = RollerPitch * 0.5f; s < len; s += RollerPitch)
            gfx.Draw(roller * Matrix4x4.CreateTranslation(a + z * s + y * 0.03f), ConvRoller);
        // Chevrons on each rail's top, the tip ahead.
        var side = width * 0.5f + 0.03f;
        var left = Matrix4x4.CreateScale(0.012f, 0.01f, 0.07f) * Matrix4x4.CreateRotationY(0.5f) * basis;
        var right = Matrix4x4.CreateScale(0.012f, 0.01f, 0.07f) * Matrix4x4.CreateRotationY(-0.5f) * basis;
        for (var s = MathF.Min(MarkPitch * 0.5f, len * 0.5f); s < len; s += MarkPitch)
            for (var e = -1; e <= 1; e += 2)
            {
                var at = a + z * s + x * (side * e) + y * 0.0555f;
                gfx.Draw(left * Matrix4x4.CreateTranslation(at - x * 0.0145f), ConvMark);
                gfx.Draw(right * Matrix4x4.CreateTranslation(at + x * 0.0145f), ConvMark);
            }
    }

    /// <summary>A conveyor's under-plate and side rails over <paramref name="len"/> about <paramref name="mid"/>.</summary>
    private static void Bed(Renderer gfx, Matrix4x4 basis, Vector3 x, Vector3 y, Vector3 mid, float len, float width, Vector3 rail, float dim)
    {
        var side = width * 0.5f + 0.03f;
        gfx.Draw(Matrix4x4.CreateScale(width + 0.06f, 0.03f, len) * basis * Matrix4x4.CreateTranslation(mid - y * 0.075f), ConvPlate * dim);
        gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.14f, len) * basis * Matrix4x4.CreateTranslation(mid + x * side - y * 0.02f), rail);
        gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.14f, len) * basis * Matrix4x4.CreateTranslation(mid - x * side - y * 0.02f), rail);
    }

    /// <summary>A conveyor's bend: a turn plate under both beds, its top just under the rails'.</summary>
    private static void ConveyorElbow(Renderer gfx, Vector3 at, Vector3 from, Vector3 to, float width, uint paint)
    {
        var mid = Vector3.Normalize(from + to);
        var basis = Basis(mid.LengthSquared() > 1e-6f ? mid : from);
        var y = new Vector3(basis.M21, basis.M22, basis.M23);
        var size = width + 0.13f;
        gfx.Draw(Matrix4x4.CreateScale(size, 0.11f, size) * basis * Matrix4x4.CreateTranslation(at - y * 0.01f), (paint != 0 ? ConstructPaint.ToVector(paint) : ConvRail) * 0.85f);
    }

    /// <summary>
    /// The pieces riding a body's conveyors, at true size, each moved on from its last known front at the conveyor's speed
    /// (no further than its link's end or the piece ahead), on the segment its middle is over. Allocates nothing.
    /// </summary>
    private static void DrawRiders(Renderer gfx, Construct c)
    {
        var elapsed = (float)Math.Clamp(ConstructConveyor.Clock - c.ConveyedStamp, 0, 0.25);
        Span<float> tail = stackalloc float[Construct.MaxRoutes];
        ConstructConveyor.Tails(c, tail);
        var prevRoute = -1;
        var prevBack = 0f;
        var routeLen = 0f;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            var k = r.Route;
            if (k >= c.RouteCount || c.Routes[k].Type != RouteType.Conveyor || ConstructParts.Get(r.Def) is not { } d)
                continue;
            if (k != prevRoute)
                routeLen = ConstructConveyor.Length(c, k);
            ConstructConveyor.Size(d, out _, out _, out var along);
            var limit = k != prevRoute ? routeLen - (tail[k] > 0 ? tail[k] + ConstructConveyor.Gap : 0) : prevBack - ConstructConveyor.Gap;
            var front = MathF.Max(r.At, MathF.Min(r.At + ConstructConveyor.Speed * elapsed, limit));
            prevRoute = k;
            prevBack = front - along;
            ConstructConveyor.Pose(c, k, front - along * 0.5f, d, out var origin, out var rot, out var seg, r.ViaPlus1 - 1);
            var m = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin) * SegFrames[seg >= 0 && seg <= c.JointCount && seg <= Construct.MaxJoints ? seg : 0];
            var paint = r.Paint != 0 ? ConstructPaint.ToVector(r.Paint) : Vector3.Zero;
            for (var bx = 0; bx < d.Boxes.Length; bx++)
            {
                ref readonly var box = ref d.Boxes[bx];
                if (box.Hidden)
                    continue;
                gfx.Draw(box.Model(0, box.Center) * m, r.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
            }
        }
    }

    /// <summary>
    /// What a body's Piece Racks hold: each piece at true size where <see cref="ConstructConveyor.RackLayout"/> lays it, at most
    /// <see cref="ConstructConveyor.RackDrawn"/> a rack, from the store as heard. Allocates nothing; an empty rack costs a look at its store.
    /// </summary>
    private static void DrawRacks(Renderer gfx, ConstructWorld world, Construct c)
    {
        Span<ConstructConveyor.RackSlot> slots = stackalloc ConstructConveyor.RackSlot[ConstructConveyor.RackDrawn];
        for (var k = 0; k < c.PieceRackCount; k++)
        {
            int part = c.PieceRacks[k];
            if (part >= c.PartCount || world.StoreOf(c, part) is not { KindN: > 0 } st)
                continue;
            var n = ConstructConveyor.RackLayout(c.DefOf(part), st, slots);
            ref var p = ref c.Parts[part];
            var frame = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * SegFrames[p.Seg <= c.JointCount && p.Seg <= Construct.MaxJoints ? p.Seg : 0];
            for (var i = 0; i < n; i++)
            {
                var d = ConstructParts.Def(slots[i].Def);
                var m = Matrix4x4.CreateTranslation(slots[i].Origin) * frame;
                for (var bx = 0; bx < d.Boxes.Length; bx++)
                {
                    ref readonly var box = ref d.Boxes[bx];
                    if (!box.Hidden)
                        gfx.Draw(box.Model(0, box.Center) * m, box.Color, shape: box.Shape);
                }
            }
        }
    }

    /// <summary>
    /// A chute's flexible section: a ribbed bellows on a curve from <paramref name="a"/> to <paramref name="b"/>, leaving each
    /// end the way it leaves it at rest, going from the duct's colour to red as it nears its limit (ConstructFlexChute.cs).
    /// </summary>
    private static void Bellows(Renderer gfx, Construct c, int flex, Vector3 a, Vector3 b, float radius, uint paint)
    {
        ref var f = ref c.FlexSections![flex];
        var strain = ConstructRoutes.FlexStrain(c, flex, out _, out _);
        ConstructRoutes.FlexEnds(c, flex, out var fromA, out var fromB);
        ConstructRoutes.Point(c, f.Route, f.Seg - 1, out var sa);
        ConstructRoutes.Point(c, f.Route, f.Seg, out var sb);
        var len = Vector3.Distance(a, b);
        var ta = Vector3.Normalize(Vector3.TransformNormal(fromA, SegFrames[sa])) * (len / 3);
        var tb = Vector3.Normalize(Vector3.TransformNormal(fromB, SegFrames[sb])) * (len / 3);
        var (c1, c2) = (a + ta, b - tb);
        var ribs = Math.Clamp((int)(len / 0.1f), 4, 14);
        var colour = paint != 0 ? ConstructPaint.ToVector(paint) : Colour(RouteType.Chute);
        colour = Vector3.Lerp(colour, new Vector3(0.85f, 0.14f, 0.1f), Math.Clamp((strain - 0.7f) / 0.3f, 0, 1));
        var prev = a;
        for (var s = 1; s <= ribs; s++)
        {
            var t = s / (float)ribs;
            var u = 1 - t;
            var p = u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b;
            var d = p - prev;
            var l = d.Length();
            if (l > 1e-4f)
            {
                var basis = Basis(d / l);
                gfx.Draw(Matrix4x4.CreateScale(radius * 1.7f, radius * 1.7f, l) * basis * Matrix4x4.CreateTranslation((prev + p) * 0.5f), colour * 0.8f);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.15f, radius * 2.15f, MathF.Min(0.035f, l * 0.5f)) * basis * Matrix4x4.CreateTranslation(prev + d * 0.5f), colour);
            }
            prev = p;
        }
        LastBellows++;
    }

    /// <summary>The remover tool's marked links, red and through the parts (unlit; allocation-free).</summary>
    public static void DrawSelected(Renderer gfx, RemoverSelection sel, float alpha, bool blendTicks)
    {
        gfx.SetDepthTest(false);
        for (var i = 0; i < sel.Count; i++)
        {
            var it = sel[i];
            var c = it.Ref;
            if (!it.Route || it.Index >= c.RouteCount)
                continue;
            ref var r = ref c.Routes[it.Index];
            if ((ushort)(((int)r.Type << 8) | r.To) != it.Def)
                continue;
            Frames(c, alpha, blendTicks);
            var a = At(c, it.Index, -1);
            for (var s = 0; s <= r.Count; s++)
            {
                var b = At(c, it.Index, s);
                Line(gfx, a, b, new Vector3(1f, 0.22f, 0.18f), 0.85f, ConstructRoutes.Radius(r.Type) * 1.1f);
                a = b;
            }
        }
        gfx.SetDepthTest(true);
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
            var colour = PreviewBusy ? new Vector3(0.6f, 0.75f, 1f) : PreviewOk ? new Vector3(0.35f, 1f, 0.45f) : new Vector3(1f, 0.3f, 0.25f);
            var radius = ConstructRoutes.Radius(PreviewType);
            for (var i = 1; i < PreviewCount; i++)
            {
                var state = i == PreviewCount - 1 || PreviewFrom > 0 && i >= PreviewFrom;
                if (!state && i + PreviewTail < PreviewCount)
                    continue; // (a long link's far nodes: they were laid long ago, and this draws a piece at a time)
                if (float.IsNaN(PreviewPts[i - 1].X) || float.IsNaN(PreviewPts[i].X))
                    continue; // (the gap between two links of a bundle)
                var a = Vector3.Transform(PreviewPts[i - 1], SegFrames[Math.Min(PreviewSeg[i - 1], (byte)Construct.MaxJoints)]);
                var b = Vector3.Transform(PreviewPts[i], SegFrames[Math.Min(PreviewSeg[i], (byte)Construct.MaxJoints)]);
                Line(gfx, a, b, state ? colour : new Vector3(0.9f, 0.9f, 0.4f), 0.75f, radius);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f) * Matrix4x4.CreateTranslation(b), state ? colour : new Vector3(1f, 0.85f, 0.3f), 0.85f, mode: 2);
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
        var p = ConstructRoutes.LivePoint(c, route, i, out var seg);
        return Vector3.Transform(p, SegFrames[seg >= 0 && seg <= c.JointCount && seg <= Construct.MaxJoints ? seg : 0]);
    }

    public static Vector3 Colour(RouteType t) => t switch
    {
        RouteType.Data => new Vector3(0.18f, 0.42f, 0.26f),
        RouteType.Power => new Vector3(0.12f, 0.12f, 0.13f),
        RouteType.Pipe => new Vector3(0.62f, 0.64f, 0.68f), // one neutral pipe for every fluid (decision 28)
        RouteType.Shaft => new Vector3(0.5f, 0.52f, 0.55f),
        RouteType.Feed => new Vector3(0.42f, 0.38f, 0.14f),
        RouteType.Conveyor => ConvRail,
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
            case RouteType.Feed:
                // A flat belt in a yellow-striped chute: it has to read as something not to shoot.
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.4f, radius * 1.3f, len) * basis * at, colour);
                gfx.Draw(Matrix4x4.CreateScale(radius * 0.6f, radius * 1.34f, len * 0.999f) * basis * at, new Vector3(0.85f, 0.65f, 0.1f) * (dim ? 0.55f : 1f));
                break;
            default: // the chute: a square duct with a rim at each end
                gfx.Draw(Matrix4x4.CreateScale(radius * 2f, radius * 2f, len) * basis * at, colour);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.2f, radius * 2.2f, 0.04f) * basis * Matrix4x4.CreateTranslation(a + z * 0.02f), colour * 0.7f);
                gfx.Draw(Matrix4x4.CreateScale(radius * 2.2f, radius * 2.2f, 0.04f) * basis * Matrix4x4.CreateTranslation(b - z * 0.02f), colour * 0.7f);
                break;
        }
    }

    /// <summary>A half shaft's ends: the sleeve its rod plunges into as the hub moves, and the CV boot at the hub.</summary>
    private static void Plunge(Renderer gfx, Vector3 a, Vector3 b, Vector3 z, float len, float radius)
    {
        var steel = new Vector3(0.32f, 0.33f, 0.35f);
        var sleeve = MathF.Min(0.45f * len, 0.4f);
        var d = radius * 2.5f;
        gfx.Draw(Matrix4x4.CreateScale(d, d, sleeve) * Basis(z) * Matrix4x4.CreateTranslation(a + z * (sleeve * 0.5f)), steel);
        var boot = MathF.Min(0.25f * len, radius * 3f);
        gfx.Draw(Matrix4x4.CreateScale(d * 1.1f, d * 1.1f, boot) * Basis(z) * Matrix4x4.CreateTranslation(b - z * (boot * 0.5f)), new Vector3(0.12f, 0.12f, 0.13f));
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
