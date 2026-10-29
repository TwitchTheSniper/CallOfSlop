using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the Sandbox constructs: every part box at its body's pose (joints turned, wheels down on their springs, turning
/// and steering), damaged parts darker and wrecked ones near black; the ghost sockets of the held part (blended); the
/// tool in your hands. Constructs are few (at most <see cref="ConstructWorld.MaxBodies"/> bodies, a few hundred boxes), so
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
        }
        LastBoxes = n;
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
        c.SegPose(seg, out var q, out var t);
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].Angle = keep[j];
        return Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
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
        gfx.Draw(Matrix4x4.CreateScale(0.03f, 0.03f, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour, opacity, mode: 2);
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

    /// <summary>The tool in your hands (drawn in the world just in front of the eye, low on the right).</summary>
    public static void DrawTool(Renderer gfx, ConstructTool tool, Vector3 eye, Vector3 fwd, double now, float progress)
    {
        if (tool == ConstructTool.None)
            return;
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        var basis = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -fwd.X, -fwd.Y, -fwd.Z, 0, 0, 0, 0, 1);
        var work = progress > 0 ? MathF.Sin((float)now * 18f) * 0.02f : 0f;
        var at = eye + fwd * (0.55f + work) + right * 0.28f - up * 0.24f;
        var frame = basis * Matrix4x4.CreateTranslation(at);
        if (tool == ConstructTool.Wrench)
        {
            gfx.Draw(Matrix4x4.CreateScale(0.035f, 0.035f, 0.32f) * Matrix4x4.CreateTranslation(0, 0, -0.08f) * frame, new Vector3(0.62f, 0.64f, 0.68f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.1f, 0.03f, 0.06f) * Matrix4x4.CreateTranslation(0, 0, -0.26f) * frame, new Vector3(0.7f, 0.72f, 0.76f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.045f, 0.045f, 0.12f) * Matrix4x4.CreateTranslation(0, 0, 0.06f) * frame, new Vector3(0.75f, 0.2f, 0.15f), 1f, mode: 0);
        }
        else if (tool == ConstructTool.Linker)
        {
            // A reel of wire with a probe.
            gfx.Draw(Matrix4x4.CreateScale(0.1f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 0, 0.02f) * frame, new Vector3(0.2f, 0.45f, 0.95f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.03f, 0.03f, 0.2f) * Matrix4x4.CreateTranslation(0, 0, -0.12f) * frame, new Vector3(0.85f, 0.75f, 0.2f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.04f, 0.13f, 0.05f) * Matrix4x4.CreateTranslation(0, -0.09f, 0.04f) * frame, new Vector3(0.18f, 0.18f, 0.2f), 1f, mode: 0);
        }
        else
        {
            gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.07f, 0.22f) * Matrix4x4.CreateTranslation(0, 0, -0.02f) * frame, new Vector3(0.95f, 0.65f, 0.15f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.025f, 0.025f, 0.16f) * Matrix4x4.CreateTranslation(0, -0.01f, -0.2f) * frame, new Vector3(0.3f, 0.3f, 0.32f), 1f, mode: 0);
            gfx.Draw(Matrix4x4.CreateScale(0.04f, 0.12f, 0.05f) * Matrix4x4.CreateTranslation(0, -0.08f, 0.04f) * frame, new Vector3(0.18f, 0.18f, 0.2f), 1f, mode: 0);
            if (progress > 0)
                gfx.Draw(Matrix4x4.CreateScale(0.05f + 0.03f * MathF.Sin((float)now * 40f)) * Matrix4x4.CreateTranslation(0, -0.01f, -0.3f) * frame, new Vector3(0.7f, 0.9f, 1f), 0.9f, mode: 2);
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
