using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the Sandbox constructs: every part box at its body's pose (joints turned, wheels down on their springs, turning
/// and steering), damaged parts darker and wrecked ones near black; the ghost sockets of the held part (blended). The
/// tools in the hands are ToolRender.cs. Constructs are few (at most <see cref="ConstructWorld.MaxBodies"/> bodies, a few hundred boxes), so
/// each box is one draw like the movers'; lit without the world-space texture, which would swim over a moving surface.
/// </summary>
public static class ConstructRender
{
    /// <summary>Boxes drawn last frame (smoke log).</summary>
    public static int LastBoxes { get; private set; }

    // The client's ghost sockets this frame (Program.Sandbox.cs fills them; the blended pass draws them).
    public static readonly ConstructGhost[] Ghosts = new ConstructGhost[ConstructBuild.MaxGhosts];
    public static int GhostCount;
    public static int GhostSelected = -1;
    public static Construct? GhostBody;
    public static ConstructPartDef? GhostDef;
    public static bool GhostValid; // the selected one fits as shown
    public static Vector3? PlaceAt; // a new construct on the ground
    public static float PlaceYaw;
    public static Construct? AimBody; // the part under the crosshair (outlined)
    public static int AimPart = -1;
    public static bool ShowWires; // the linking tool is out: the wires are drawn (only then)
    public static ushort WireFromBody; // a wire in progress starts at this part
    public static int WireFromPart = -1;
    public static Vector3 WireEye, WireAim; // (only the wires of constructs near the eye; a wire in progress runs to the aim)
    public static int LastWires { get; private set; }

    public static void ClearAim()
    {
        GhostCount = 0;
        GhostSelected = -1;
        GhostBody = null;
        GhostDef = null;
        PlaceAt = null;
        AimBody = null;
        AimPart = -1;
        ShowWires = false;
        WireFromBody = 0;
        WireFromPart = -1;
    }

    /// <summary>Every construct at this frame's pose: <paramref name="alpha"/> blends the last two ticks (the host); a client's poses are blended already.</summary>
    public static void Draw(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        foreach (var c in world.Bodies)
        {
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
            for (var i = 0; i < c.PartCount; i++)
                n += DrawPart(gfx, c, i, body, blendTicks ? alpha : 1f, 1f);
            n += DrawWheel(gfx, c, alpha, blendTicks);
        }
        n += DrawCouplings(gfx, world, alpha, blendTicks);
        LastBoxes = n;
    }

    /// <summary>A point on part <paramref name="i"/> (in its own frame) where it is drawn this frame.</summary>
    public static Vector3 DrawnPoint(Construct c, int i, Vector3 local, float alpha, bool blendTicks)
    {
        ref var p = ref c.Parts[i];
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var sq = Quaternion.Identity;
        var st = Vector3.Zero;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
            SegPoseBlended(c, p.Seg, blendTicks ? alpha : 1f, out sq, out st);
        return pos + Vector3.Transform(st + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), sq), rot);
    }

    /// <summary>Boxes in the last frame's chains (the self-test).</summary>
    public static int LastChainLinks { get; private set; }

    /// <summary>
    /// The couplings (milestone 3): a chain from its hook to the eye, sagging when it is slack (links turned alternately),
    /// and a short pin through a tow ball's eye. Drawn where both ends are this frame.
    /// </summary>
    private static int DrawCouplings(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        LastChainLinks = 0;
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is not { } a || world.Get(cp.BodyB) is not { } b || cp.PartA >= a.PartCount || cp.PartB >= b.PartCount)
                continue;
            var pa = DrawnPoint(a, cp.PartA, a.DefOf(cp.PartA).HitchAt, alpha, blendTicks);
            var pb = DrawnPoint(b, cp.PartB, b.DefOf(cp.PartB).HitchAt, alpha, blendTicks);
            var steel = new Vector3(0.32f, 0.33f, 0.35f);
            if (cp.Length <= 0)
            {
                gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.14f, 0.05f) * Matrix4x4.CreateTranslation((pa + pb) * 0.5f), steel, 1f, mode: 0); // (the pin)
                n++;
                continue;
            }
            // A slack chain sags: its middle hangs down by about how much longer it is than the span.
            var span = Vector3.Distance(pa, pb);
            var sag = span < cp.Length ? MathF.Sqrt(MathF.Max(0, cp.Length * cp.Length - span * span)) * 0.5f : 0f;
            var mid = (pa + pb) * 0.5f - new Vector3(0, sag, 0);
            var links = Math.Clamp((int)(cp.Length / 0.12f), 4, 64);
            var prev = pa;
            for (var i = 1; i <= links; i++)
            {
                var t = i / (float)links;
                var u = 1 - t;
                var at = u * u * pa + 2 * u * t * mid + t * t * pb; // (a quadratic curve through the sag)
                Link(gfx, prev, at, steel, (i & 1) == 0);
                prev = at;
                n++;
            }
            LastChainLinks += links;
        }
        return n;
    }

    /// <summary>One chain link from <paramref name="a"/> to <paramref name="b"/>, flat one way or turned a quarter.</summary>
    private static void Link(Renderer gfx, Vector3 a, Vector3 b, Vector3 colour, bool turned)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var size = turned ? new Vector3(0.018f, 0.045f, len * 1.15f) : new Vector3(0.045f, 0.018f, len * 1.15f);
        gfx.Draw(Matrix4x4.CreateScale(size) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, 1f, mode: 0);
    }

    /// <summary>A gauge's needle (milestone 3): from its hub on the dial's face, swept 270 degrees from empty to full scale by its signal.</summary>
    private static void DrawNeedle(Renderer gfx, ConstructPartDef d, in CPart p, in Matrix4x4 partToBody)
    {
        var n = ConstructParts.DirVector(d.DialDir);
        var up = MathF.Abs(n.Y) > 0.5f ? -Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Cross(up, n);
        var share = Math.Clamp(p.Out / d.DialMax, 0, 1);
        var a = (-135f + 270f * share) * MathF.PI / 180f;
        var dir = up * MathF.Cos(a) + right * MathF.Sin(a);
        var x = Vector3.Cross(dir, n);
        const float len = 0.075f;
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, dir.X, dir.Y, dir.Z, 0, n.X, n.Y, n.Z, 0, 0, 0, 0, 1);
        var at = d.DialAt + dir * (len * 0.45f) + n * 0.008f;
        gfx.Draw(Matrix4x4.CreateScale(0.012f, len, 0.008f) * basis * Matrix4x4.CreateTranslation(at) * partToBody, new Vector3(0.95f, 0.25f, 0.15f), 1f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(d.DialAt + n * 0.008f) * partToBody, new Vector3(0.15f, 0.15f, 0.15f), 1f, mode: 0); // (the hub)
    }

    private static int DrawPart(Renderer gfx, Construct c, int i, in Matrix4x4 body, float alpha, float opacity)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        // The segment's pose (a door turned on its hinge).
        var seg = Matrix4x4.Identity;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
        {
            seg = SegMatrix(c, p.Seg, alpha);
        }
        // A raycast wheel: down its suspension, turning, steering.
        var wheel = Matrix4x4.Identity;
        var spinAxis = Vector3.UnitX;
        var spin = 0f;
        var isWheel = false;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Part != i)
                continue;
            isWheel = true;
            var drop = w.Compression - w.Travel * 0.5f; // (the part sits at mid-travel; compressed: higher)
            wheel = Matrix4x4.CreateTranslation(0, drop, 0); // (steering: the wheel's segment turns it, a steering pivot)
            spin = w.Spin;
            spinAxis = w.Axle;
            break;
        }
        var health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
        var shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
        var drawn = 0;
        var lit = d.Logic == CLogic.Lamp && p.Out >= 0.5f && !p.Broken; // a lamp that is on glows (unlit, bright)
        if (d.HasDial && !p.Broken)
        {
            DrawNeedle(gfx, d, p, Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * seg * body);
            drawn += 2;
        }
        // A piston's rod, from its base up to where its leaf has slid to.
        if (d.Role == PartRole.Joint && d.JointType == JointType.Piston && c.JointOf(i) is var pj and >= 0 && c.Joints[pj].Angle > 0.01f)
        {
            ref var jt = ref c.Joints[pj];
            var ext = float.Lerp(jt.PrevAngle, jt.Angle, alpha);
            c.SocketAt(i, d.LeafIndex, out var leaf, out _);
            var mid = leaf + jt.Axis * (ext * 0.5f);
            var size = new Vector3(0.12f) + Vector3.Abs(jt.Axis) * (ext - 0.12f);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(mid) * seg * body, new Vector3(0.78f, 0.8f, 0.84f) * shade, 1f, mode: 0);
            drawn++;
        }
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            c.BoxOf(i, b, out var centre, out var half);
            var model = Matrix4x4.CreateScale(half * 2);
            if (isWheel)
            {
                // Turn about the axle through the wheel's centre.
                c.PartBounds(i, out var mn, out var mx);
                var hub = (mn + mx) * 0.5f;
                model *= Matrix4x4.CreateTranslation(centre - hub) * Matrix4x4.CreateFromAxisAngle(spinAxis, spin) * Matrix4x4.CreateTranslation(hub);
            }
            else
                model *= Matrix4x4.CreateTranslation(centre);
            model = model * wheel * seg * body;
            if (lit)
                gfx.Draw(model, new Vector3(1f, 0.93f, 0.6f), opacity, mode: 2);
            else
                gfx.Draw(model, d.Boxes[b].Color * shade, opacity, mode: 0);
            drawn++;
        }
        return drawn;
    }

    /// <summary>A segment's pose (rest frame -> body frame) blended between the last two ticks' joint values.</summary>
    public static Matrix4x4 SegMatrix(Construct c, int seg, float alpha)
    {
        SegPoseBlended(c, seg, alpha, out var q, out var t);
        return Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>
    /// Where a seat is drawn this frame, exactly as <see cref="Draw"/> places its boxes: the seat's turn in the world (its
    /// -Z the way the sitter faces, its +Y their up) and the sitter's feet (the seat's "at" point). A seated player is
    /// drawn, and the local camera put, from this, so they ride as part of the construct (no lag, no slide).
    /// </summary>
    public static void SeatPose(Construct c, int part, float alpha, bool blendTicks, out Quaternion rot, out Vector3 feet)
    {
        ref var p = ref c.Parts[part];
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var body = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var sq = Quaternion.Identity;
        var st = Vector3.Zero;
        if (p.Seg > 0 && p.Seg <= c.JointCount)
            SegPoseBlended(c, p.Seg, blendTicks ? alpha : 1f, out sq, out st);
        rot = Quaternion.Normalize(body * sq * Rot24.Quat(p.Rot));
        feet = pos + Vector3.Transform(st + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.SeatAt), sq), body);
    }

    /// <summary>The steering wheel in front of a drive seat, from the sitter's feet in the seat's frame: the rim's centre (tilted back to face the driver).</summary>
    public static readonly Vector3 WheelAt = new(0, 1.12f, -0.5f);

    public const float WheelTilt = 0.55f, WheelRadius = 0.17f;

    /// <summary>Where the driver's hands hold the steering wheel (world), turned by the steering: right hand, left hand, and the rim's turn.</summary>
    public static void WheelGrips(Construct c, in Quaternion seat, Vector3 feet, out Vector3 right, out Vector3 left, out Quaternion rim)
    {
        rim = seat * Quaternion.CreateFromAxisAngle(Vector3.UnitX, WheelTilt) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -c.Steer * 2.5f);
        var centre = feet + Vector3.Transform(WheelAt, seat);
        right = centre + Vector3.Transform(new Vector3(WheelRadius, 0, 0), rim);
        left = centre + Vector3.Transform(new Vector3(-WheelRadius, 0, 0), rim);
    }

    /// <summary>The steering wheel of every drive seat (a rim of four bars, a hub and a column), turned by the steering.</summary>
    private static int DrawWheel(Renderer gfx, Construct c, float alpha, bool blendTicks)
    {
        if (c.DriveSeat < 0 || c.DriveSeat >= c.PartCount || c.Parts[c.DriveSeat].Broken)
            return 0;
        SeatPose(c, c.DriveSeat, alpha, blendTicks, out var seat, out var feet);
        WheelGrips(c, seat, feet, out var r, out var l, out var rim);
        var centre = (r + l) * 0.5f;
        var frame = Matrix4x4.CreateFromQuaternion(rim) * Matrix4x4.CreateTranslation(centre);
        var dark = new Vector3(0.12f, 0.12f, 0.13f);
        const float k = WheelRadius, t = 0.035f;
        gfx.Draw(Matrix4x4.CreateScale(t, k * 2 + t, t) * Matrix4x4.CreateTranslation(k, 0, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(t, k * 2 + t, t) * Matrix4x4.CreateTranslation(-k, 0, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2 + t, t, t) * Matrix4x4.CreateTranslation(0, k, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2 + t, t, t) * Matrix4x4.CreateTranslation(0, -k, 0) * frame, dark, 1f, mode: 0);
        gfx.Draw(Matrix4x4.CreateScale(k * 2, 0.03f, 0.02f) * frame, new Vector3(0.3f, 0.3f, 0.32f), 1f, mode: 0); // spoke
        // The column, from the hub down and forward to the seat's front.
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.05f, 0.4f) * Matrix4x4.CreateTranslation(0, 0, -0.2f) * frame, dark, 1f, mode: 0);
        return 6;
    }

    /// <summary>A segment's pose (rest frame -> body frame) with every joint up its chain blended between its last two ticks' values.</summary>
    public static void SegPoseBlended(Construct c, int seg, float alpha, out Quaternion q, out Vector3 t)
    {
        // Every joint up the chain at its blended value (a turntable that wrapped round blends the short way).
        Span<float> keep = stackalloc float[Construct.MaxJoints];
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            keep[j] = jt.Angle;
            var from = jt.PrevAngle;
            if (!jt.Slides && MathF.Abs(jt.Angle - from) > MathF.PI)
                from += MathF.Sign(jt.Angle - from) * MathF.Tau;
            jt.Angle = float.Lerp(from, jt.Angle, alpha);
        }
        c.SegPose(seg, out q, out t);
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Angle = keep[j];
    }

    /// <summary>A part's centre in the world (its segment as it is now).</summary>
    public static Vector3 PartCentre(Construct c, int i)
    {
        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
        c.PartBounds(i, out var mn, out var mx);
        return t + Vector3.Transform((mn + mx) * 0.5f, q);
    }

    /// <summary>The wires, while the linking tool is out (unlit lines between the parts' centres: lit when the signal is on), and a wire in progress.</summary>
    public static void DrawWires(Renderer gfx, ConstructWorld world)
    {
        LastWires = 0;
        if (!ShowWires)
            return;
        gfx.SetDepthTest(false); // (seen through the parts: the wires run inside the construct)
        foreach (var c in world.Bodies)
        {
            if (c.WireCount == 0 && c.Id != WireFromBody)
                continue;
            if (Vector3.DistanceSquared(c.WorldCom, WireEye) > (25f + c.Radius) * (25f + c.Radius))
                continue;
            for (var k = 0; k < c.WireCount; k++)
            {
                ref var w = ref c.Wires[k];
                if (w.From >= c.PartCount || w.To >= c.PartCount)
                    continue;
                var a = PartCentre(c, w.From);
                var b = PartCentre(c, w.To);
                var on = c.Parts[w.From].Out >= 0.5f;
                if (w.From == w.To)
                    b = a + new Vector3(0, 0.25f, 0); // (a part on itself: a little loop up)
                Line(gfx, a, b, on ? new Vector3(0.4f, 1f, 0.45f) : new Vector3(0.2f, 0.45f, 1f), 0.9f);
                // The input end: a small block.
                gfx.Draw(Matrix4x4.CreateScale(0.07f) * Matrix4x4.CreateTranslation(b), new Vector3(1f, 0.8f, 0.3f), 0.9f, mode: 2);
                LastWires++;
            }
            if (c.Id == WireFromBody && WireFromPart >= 0 && WireFromPart < c.PartCount)
                Line(gfx, PartCentre(c, WireFromPart), WireAim, new Vector3(1f, 0.9f, 0.3f), 0.7f);
        }
        // Wires between constructs (milestone 3): the same, in orange while off.
        for (var k = 0; k < world.XWireCount; k++)
        {
            ref var x = ref world.XWires[k];
            if (world.Get(x.BodyFrom) is not { } cf || world.Get(x.BodyTo) is not { } ct || x.From >= cf.PartCount || x.To >= ct.PartCount)
                continue;
            var a = PartCentre(cf, x.From);
            var b = PartCentre(ct, x.To);
            if (Vector3.DistanceSquared(a, WireEye) > 900f)
                continue;
            Line(gfx, a, b, cf.Parts[x.From].Out >= 0.5f ? new Vector3(0.4f, 1f, 0.45f) : new Vector3(1f, 0.55f, 0.2f), 0.9f);
            gfx.Draw(Matrix4x4.CreateScale(0.07f) * Matrix4x4.CreateTranslation(b), new Vector3(1f, 0.8f, 0.3f), 0.9f, mode: 2);
            LastWires++;
        }
        gfx.SetDepthTest(true);
    }

    /// <summary>A thin unlit box from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Line(Renderer gfx, Vector3 a, Vector3 b, Vector3 colour, float opacity)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(0.035f, 0.035f, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, opacity, mode: 2);
    }

    /// <summary>The ghost sockets (blended, depth-tested, no depth write): small markers on every free socket the held part fits, the chosen one as the whole part.</summary>
    public static void DrawGhosts(Renderer gfx, double now)
    {
        var pulse = 0.55f + 0.25f * MathF.Sin((float)now * 6f);
        if (GhostBody is { } c && GhostDef is { } def)
        {
            var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
            for (var k = 0; k < GhostCount; k++)
            {
                ref var g = ref Ghosts[k];
                c.SocketAt(g.Part, g.Socket, out var at, out var dir);
                c.SegPose(c.Parts[g.Part].Seg, out var q, out var t);
                var seg = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
                var colour = k == GhostSelected ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(0.35f, 0.75f, 1f);
                gfx.Draw(Matrix4x4.CreateScale(0.14f) * Matrix4x4.CreateTranslation(at + ConstructParts.DirVector(dir) * 0.04f) * seg * body, colour, k == GhostSelected ? 0.9f : 0.55f, mode: 2);
                if (k != GhostSelected)
                    continue;
                // The whole part where it would go.
                for (var b = 0; b < def.Boxes.Length; b++)
                {
                    var bc = g.Pos + Rot24.Apply(g.Rot, def.Boxes[b].Center);
                    var bh = Rot24.ApplyHalf(g.Rot, def.Boxes[b].Half);
                    gfx.Draw(Matrix4x4.CreateScale(bh * 2 + new Vector3(0.01f)) * Matrix4x4.CreateTranslation(bc) * seg * body, GhostValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f), 0.35f * pulse, mode: 2);
                }
            }
        }
        if (PlaceAt is { } ground && GhostDef is { } nd)
        {
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -PlaceYaw);
            var origin = ground - Vector3.Transform(new Vector3(0, nd.BoundsMin.Y - 0.01f, 0), rot);
            var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin);
            for (var b = 0; b < nd.Boxes.Length; b++)
                gfx.Draw(Matrix4x4.CreateScale(nd.Boxes[b].Half * 2 + new Vector3(0.01f)) * Matrix4x4.CreateTranslation(nd.Boxes[b].Center) * body, new Vector3(0.35f, 1f, 0.5f), 0.3f * pulse, mode: 2);
        }
        if (AimBody is { } ab && AimPart >= 0 && AimPart < ab.PartCount)
        {
            ab.WorldFrame(ab.Parts[AimPart].Seg, out var q, out var t);
            ab.PartBounds(AimPart, out var mn, out var mx);
            var m = Matrix4x4.CreateScale(mx - mn + new Vector3(0.04f)) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
            gfx.Draw(m, new Vector3(1f, 0.85f, 0.3f), 0.18f, mode: 2);
        }
    }

    /// <summary>The wheels' springs on a client (or the host's drawing between ticks): where each wheel meets the ground now.</summary>
    public static void FitWheels(ConstructWorld world, IWorldQuery query)
    {
        foreach (var c in world.Bodies)
        {
            var up = Vector3.Transform(Vector3.UnitY, c.Rot);
            for (var k = 0; k < c.WheelCount; k++)
            {
                ref var w = ref c.Wheels[k];
                var len = w.Travel + w.Radius;
                c.WheelFrame(k, out var centre, out _);
                var d = query.Raycast(c.ToWorld(centre), -up, len, QueryFilter.All);
                w.Compression = MathF.Min(len - d, w.Travel);
                w.Touching = d < len;
            }
        }
    }
}
