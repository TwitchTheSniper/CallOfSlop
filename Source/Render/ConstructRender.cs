using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the Sandbox constructs: every part box at its body's pose (joints turned, wheels down on their springs, turning
/// and steering), damaged parts darker and wrecked ones near black; the ghost sockets of the held part (blended). The
/// tools in the hands are ToolRender.cs. Constructs are few (at most <see cref="ConstructWorld.MaxBodies"/> bodies, a few hundred boxes), so
/// each box is one draw like the movers'; lit without the world-space texture, which would swim over a moving surface.
/// Glass panes (<see cref="CPartBox.Glass"/>) are left out of the opaque pass and kept for <see cref="DrawGlass"/>, which
/// draws them in the blended pass after everything opaque: back to front, without depth writes, lit and fogged by the
/// box shader like any part, with a slight tint, a little reflection at grazing angles and a highlight along the edges.
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
    public static Vector3? PlaceAt; // free placement: a new construct's origin on the surface aimed at
    public static Quaternion PlaceRot = Quaternion.Identity; // ...its frame
    public static byte PlaceTurn; // ...the part's Rot24 turn in it
    public static bool PlaceValid; // ...green when it fits, red when not
    public static Construct? AimBody; // the part under the crosshair (outlined)
    public static int AimPart = -1;
    public static bool ShowWires; // the linking tool is out: the wires are drawn (only then)
    public static ushort WireFromBody; // a wire in progress starts at this part
    public static int WireFromPart = -1;
    public static Vector3 WireEye, WireAim; // (only the wires of constructs near the eye; a wire in progress runs to the aim)
    public static int LastWires { get; private set; }

    /// <summary>The frame's clock (flames flicker, muzzle flashes fade); set before <see cref="Draw"/>.</summary>
    public static double Now;

    /// <summary>A gun's muzzle flash (milestone 3): drawn for a moment where it fired.</summary>
    public struct Flash
    {
        public Vector3 At, Dir;
        public double Born;
        public GunFx Fx;
    }

    public static readonly Flash[] Flashes = new Flash[32];
    private static int _flashNext;
    public const double FlashSeconds = 0.06;

    public static void AddFlash(Vector3 at, Vector3 dir, double now, GunFx fx)
    {
        Flashes[_flashNext] = new Flash { At = at, Dir = dir, Born = now, Fx = fx };
        _flashNext = (_flashNext + 1) % Flashes.Length;
    }

    // Physical storage (decision 18): the slots the held part fits on the storage part under the crosshair (ghost outlines).
    public static Construct? SlotBody;
    public static int SlotPart = -1;
    public static int SlotMask; // bit k: slot k takes the held part
    public static int SlotSelected = -1; // the one a click sets it into
    public static byte SlotRot; // the turn it would go in at
    public static ConstructPartDef? SlotDef; // the held part
    public static int LastStored { get; private set; } // stored parts drawn last frame (the self-test)

    public static void ClearAim()
    {
        SlotBody = null;
        SlotPart = -1;
        SlotMask = 0;
        SlotSelected = -1;
        SlotDef = null;
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
        GlassCount = 0;
        foreach (var c in world.Bodies)
        {
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
            for (var i = 0; i < c.PartCount; i++)
            {
                n += DrawPart(gfx, c, i, body, blendTicks ? alpha : 1f, 1f);
                if (c.Parts[i].Store != 0)
                    n += DrawStored(gfx, world, c, i, body, blendTicks ? alpha : 1f);
            }
            n += DrawWheel(gfx, c, alpha, blendTicks);
        }
        LastStored = LastStoredCount;
        LastStoredCount = 0;
        n += DrawCouplings(gfx, world, alpha, blendTicks);
        // Muzzle flashes (unlit, bright, a few frames).
        for (var k = 0; k < Flashes.Length; k++)
        {
            ref var f = ref Flashes[k];
            var age = Now - f.Born;
            if (f.Born <= 0 || age < 0 || age > FlashSeconds)
                continue;
            var size = f.Fx switch { GunFx.Cannon => 0.55f, GunFx.Rocket => 0.4f, GunFx.Flame => 0.35f, _ => 0.22f } * (1f - (float)(age / FlashSeconds) * 0.5f);
            gfx.Draw(Matrix4x4.CreateScale(size * 0.6f, size * 0.6f, size) * Basis(f.Dir) * Matrix4x4.CreateTranslation(f.At + f.Dir * size * 0.5f), new Vector3(1f, 0.8f, 0.35f), 0.9f, mode: 2);
            n++;
        }
        LastBoxes = n;
    }

    private static Matrix4x4 Basis(Vector3 z)
    {
        z = z.LengthSquared() > 1e-6f ? Vector3.Normalize(z) : -Vector3.UnitZ;
        var x = MathF.Abs(z.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)) : Vector3.UnitX;
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
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
        if (p.Burn > 0)
        {
            // On fire (incendiary rounds, milestone 3): a few flickering tongues of flame over its top.
            c.PartBounds(i, out var mn, out var mx);
            var top = new Vector3((mn.X + mx.X) * 0.5f, mx.Y, (mn.Z + mx.Z) * 0.5f);
            var w = MathF.Min(mx.X - mn.X, mx.Z - mn.Z);
            for (var f = 0; f < 3; f++)
            {
                var flick = 0.65f + 0.35f * MathF.Sin((float)Now * (13f + f * 4.3f) + i * 1.7f + f * 2.1f);
                var h = (0.25f + 0.2f * f) * flick;
                var off = new Vector3((f - 1) * w * 0.25f, h * 0.5f, (f == 1 ? 0.1f : -0.1f) * w);
                gfx.Draw(Matrix4x4.CreateScale(w * (0.45f - f * 0.08f), h, w * (0.45f - f * 0.08f)) * Matrix4x4.CreateTranslation(top + off) * seg * body,
                    f == 1 ? new Vector3(1f, 0.85f, 0.3f) : new Vector3(1f, 0.45f, 0.1f), 0.85f, mode: 2);
                drawn++;
            }
        }
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            ref readonly var box = ref d.Boxes[b];
            if (box.Hidden)
                continue; // (a shaped part's stepped collision: its shape is drawn instead)
            c.BoxOf(i, b, out var centre, out var half);
            Matrix4x4 model;
            if (isWheel)
            {
                // Turn about the axle through the wheel's centre.
                c.PartBounds(i, out var mn, out var mx);
                var hub = (mn + mx) * 0.5f;
                model = box.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateTranslation(centre - hub) * Matrix4x4.CreateFromAxisAngle(spinAxis, spin) * Matrix4x4.CreateTranslation(hub);
            }
            else
                model = box.Model(p.Rot, centre);
            model = model * wheel * seg * body;
            if (lit)
                gfx.Draw(model, new Vector3(1f, 0.93f, 0.6f), opacity, mode: 2, shape: box.Shape);
            else if (box.IsGlass)
            {
                if (!QueueGlass(model, box.Color * shade, box.Glass * opacity, box.Shape))
                    gfx.Draw(model, box.Color * shade, opacity, mode: 0, shape: box.Shape); // (more panes than the list holds: opaque)
                continue;
            }
            else
                gfx.Draw(model, box.Color * shade, opacity, mode: 0, shape: box.Shape);
            drawn++;
        }
        return drawn;
    }

    private static readonly Vector3 ScrapColour = new(0.43f, 0.35f, 0.28f);

    /// <summary>What a storage part holds, where it sits (each stored part's boxes in its slot, a pile stacked up), and a bin's fill level.</summary>
    private static int DrawStored(Renderer gfx, ConstructWorld world, Construct c, int i, in Matrix4x4 body, float alpha)
    {
        if (world.StoreOf(c, i) is not { } st)
            return 0;
        ref var p = ref c.Parts[i];
        var cd = p.D;
        var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) : Matrix4x4.Identity;
        var drawn = 0;
        for (var k = 0; k < st.SlotCount && k < cd.Slots.Length; k++)
        {
            if (ConstructParts.Get(st.Def[k]) is not { } d)
                continue;
            var shade = 0.55f + 0.45f * Math.Clamp(st.Health[k], 0, 1);
            var copies = ConstructStorage.DrawnCopies(cd.Slots[k], d, st.Rot[k], st.Count[k]);
            var turn = Rot24.Compose(p.Rot, st.Rot[k]);
            for (var n = 0; n < copies; n++)
                for (var b = 0; b < d.Boxes.Length; b++)
                {
                    ref readonly var box = ref d.Boxes[b];
                    if (box.Hidden)
                        continue;
                    ConstructStorage.ItemBox(p, cd.Slots[k], d, st.Rot[k], n, b, out var centre, out _);
                    if (box.IsGlass && QueueGlass(box.Model(turn, centre) * seg * body, box.Color * shade, box.Glass, box.Shape))
                        continue; // (a stored pane: see-through too)
                    gfx.Draw(box.Model(turn, centre) * seg * body, box.Color * shade, 1f, mode: 0, shape: box.Shape);
                    drawn++;
                }
            LastStoredCount++;
        }
        if (ConstructStorage.FillBox(p, cd, st.Amount, out var fc, out var fh))
        {
            gfx.Draw(Matrix4x4.CreateScale(fh * 2) * Matrix4x4.CreateTranslation(fc) * seg * body, ScrapColour, 1f, mode: 0);
            // A few lumps on top, so it reads as a heap.
            for (var l = 0; l < 3; l++)
            {
                var off = new Vector3((l - 1) * fh.X * 0.5f, fh.Y, (l == 1 ? -0.3f : 0.25f) * fh.Z);
                var size = new Vector3(fh.X * 0.35f, MathF.Min(0.06f, fh.Y), fh.Z * 0.35f);
                gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(fc + off) * seg * body, ScrapColour * (0.8f + 0.15f * l), 1f, mode: 0);
            }
            drawn += 4;
        }
        return drawn;
    }

    private static int LastStoredCount;

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
                    ref readonly var box = ref def.Boxes[b];
                    if (box.Hidden)
                        continue;
                    var bc = g.Pos + Rot24.Apply(g.Rot, box.Center);
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * box.Model(g.Rot, bc) * seg * body, GhostValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f), 0.35f * pulse * GhostGlass(box), mode: 2, shape: box.Shape);
                }
            }
        }
        if (SlotBody is { } sbody && SlotDef is { } sdef && SlotPart >= 0 && SlotPart < sbody.PartCount)
        {
            // The slots the held part fits, as ghost outlines; the chosen one with the part where it would sit.
            ref var cp = ref sbody.Parts[SlotPart];
            var cd = cp.D;
            sbody.SegPose(cp.Seg, out var q, out var t);
            var m = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t) * Matrix4x4.CreateFromQuaternion(sbody.Rot) * Matrix4x4.CreateTranslation(sbody.Pos);
            for (var k = 0; k < cd.Slots.Length; k++)
            {
                if ((SlotMask & (1 << k)) == 0)
                    continue;
                ConstructStorage.SlotBox(cp, cd.Slots[k], out var sc, out var sh);
                var chosen = k == SlotSelected;
                var colour = chosen ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(0.35f, 0.75f, 1f);
                Outline(gfx, sc, sh, m, colour, chosen ? 0.9f : 0.6f);
                gfx.Draw(Matrix4x4.CreateScale(sh * 2) * Matrix4x4.CreateTranslation(sc) * m, colour, (chosen ? 0.16f : 0.08f) * pulse * 1.6f, mode: 2);
                if (!chosen)
                    continue;
                var turn = Rot24.Compose(cp.Rot, SlotRot);
                for (var b = 0; b < sdef.Boxes.Length; b++)
                {
                    if (sdef.Boxes[b].Hidden)
                        continue;
                    ConstructStorage.ItemBox(cp, cd.Slots[k], sdef, SlotRot, 0, b, out var bc, out _);
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * sdef.Boxes[b].Model(turn, bc) * m, new Vector3(0.35f, 1f, 0.5f), 0.35f * pulse * GhostGlass(sdef.Boxes[b]), mode: 2, shape: sdef.Boxes[b].Shape);
                }
            }
        }
        if (PlaceAt is { } ground && GhostDef is { } nd)
        {
            var body = Matrix4x4.CreateFromQuaternion(Rot24.Quat(PlaceTurn)) * Matrix4x4.CreateFromQuaternion(PlaceRot) * Matrix4x4.CreateTranslation(ground);
            var colour = PlaceValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
            for (var b = 0; b < nd.Boxes.Length; b++)
                if (!nd.Boxes[b].Hidden)
                    gfx.Draw(Matrix4x4.CreateScale(1.01f) * nd.Boxes[b].Model(0, nd.Boxes[b].Center) * body, colour, 0.3f * pulse * GhostGlass(nd.Boxes[b]), mode: 2, shape: nd.Boxes[b].Shape);
        }
        if (AimBody is { } ab && AimPart >= 0 && AimPart < ab.PartCount)
        {
            ab.WorldFrame(ab.Parts[AimPart].Seg, out var q, out var t);
            ab.PartBounds(AimPart, out var mn, out var mx);
            var m = Matrix4x4.CreateScale(mx - mn + new Vector3(0.04f)) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
            gfx.Draw(m, new Vector3(1f, 0.85f, 0.3f), 0.18f, mode: 2);
        }
    }

    /// <summary>A ghost's glass pane is fainter than the rest of it (the frame round it reads, the pane shows through).</summary>
    public static float GhostGlass(in CPartBox box) => box.IsGlass ? 0.45f : 1f;

    // ---- Glass (the blended pass) -------------------------------------------------------------------------------------

    /// <summary>The most glass boxes drawn see-through in a frame (more are drawn opaque: a few hundred panes is plenty).</summary>
    public const int MaxGlass = 512;

    private struct GlassBox
    {
        public Matrix4x4 Model;
        public Vector3 Color;
        public float Alpha;
        public ushort Shape;
    }

    private static readonly GlassBox[] _glass = new GlassBox[MaxGlass];
    private static readonly float[] _glassKey = new float[MaxGlass];
    private static readonly int[] _glassOrder = new int[MaxGlass];

    /// <summary>Glass boxes <see cref="Draw"/> kept for <see cref="DrawGlass"/> this frame.</summary>
    public static int GlassCount { get; private set; }

    /// <summary>Glass boxes (and their edge highlights) the last <see cref="DrawGlass"/> drew (the self-test).</summary>
    public static int LastGlass { get; private set; }

    /// <summary>Keeps a glass box for the blended pass; false when the list is full (then it is drawn opaque).</summary>
    public static bool QueueGlass(in Matrix4x4 model, Vector3 colour, float alpha, ushort shape)
    {
        if (GlassCount >= MaxGlass)
            return false;
        _glass[GlassCount++] = new GlassBox { Model = model, Color = colour, Alpha = Math.Clamp(alpha, 0.05f, 0.95f), Shape = shape };
        return true;
    }

    /// <summary>The highlight along a pane's edges: its width (m) and how much lighter than the pane it is.</summary>
    private const float EdgeWidth = 0.012f;

    private static readonly Vector3 GlassSheen = new(0.86f, 0.92f, 0.97f);

    /// <summary>
    /// The glass <see cref="Draw"/> kept: call it in the blended pass (it turns blending on and depth writes off, and leaves
    /// them so), after everything opaque, so what is behind a pane (a player in the cab, the far side of the yard) shows
    /// through it. Farthest first (sorted by the distance from <paramref name="eye"/>), so panes behind panes blend right.
    /// Each pane is lit and fogged by the box shader (mode 0, both GL paths); it reflects a little more and takes a sheen
    /// at grazing angles, and a thin lighter strip runs along each long edge so it still reads as glass edge-on.
    /// </summary>
    public static void DrawGlass(Renderer gfx, Vector3 eye)
    {
        var n = GlassCount;
        LastGlass = 0;
        if (n == 0)
            return;
        gfx.SetBlend(true);
        gfx.SetDepthWrite(false);
        for (var k = 0; k < n; k++)
        {
            _glassOrder[k] = k;
            _glassKey[k] = -Vector3.DistanceSquared(_glass[k].Model.Translation, eye); // (farthest first)
        }
        Array.Sort(_glassKey, _glassOrder, 0, n);
        var drawn = 0;
        for (var k = 0; k < n; k++)
        {
            ref readonly var g = ref _glass[_glassOrder[k]];
            var m = g.Model;
            var centre = m.Translation;
            // The pane's thinnest axis is its normal (the model's rows are its scaled axes).
            Span<Vector3> axes = [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
            var size = new Vector3(axes[0].Length(), axes[1].Length(), axes[2].Length());
            var thin = size.X <= size.Y && size.X <= size.Z ? 0 : size.Y <= size.Z ? 1 : 2;
            var normal = axes[thin] / MathF.Max(size[thin], 1e-5f);
            var view = centre - eye;
            var len = view.Length();
            var facing = len > 1e-4f ? MathF.Abs(Vector3.Dot(view / len, normal)) : 1f;
            var graze = 1f - facing;
            var sheen = graze * graze;
            var colour = Vector3.Lerp(g.Color, GlassSheen, 0.35f * sheen);
            var alpha = g.Alpha + (0.85f - g.Alpha) * 0.6f * sheen * graze;
            gfx.Draw(m, colour, alpha, mode: 0, shape: g.Shape);
            drawn++;
            if (g.Shape != 0)
                continue;
            // The edges: a thin strip along both ends of each of the pane's two long axes, a hair proud of its faces.
            for (var a = 0; a < 3; a++)
            {
                if (a == thin)
                    continue;
                var b = 3 - a - thin; // (the other long axis: the strip sits at its two ends)
                if (size[b] < EdgeWidth * 3)
                    continue;
                var w = EdgeWidth / size[b];
                for (var e = -1; e <= 1; e += 2)
                {
                    var scale = Vector3.One;
                    scale[b] = w;
                    scale[thin] = 1.08f;
                    var off = Vector3.Zero;
                    off[b] = e * (0.5f - w * 0.5f);
                    gfx.Draw(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(off) * m, Vector3.Lerp(g.Color, GlassSheen, 0.6f), MathF.Min(0.9f, g.Alpha + 0.3f + 0.3f * graze), mode: 0);
                    drawn++;
                }
            }
        }
        LastGlass = drawn;
    }

    /// <summary>A box's twelve edges as thin bars (a slot's ghost outline).</summary>
    private static void Outline(Renderer gfx, Vector3 c, Vector3 h, in Matrix4x4 m, Vector3 colour, float opacity)
    {
        const float w = 0.012f;
        for (var axis = 0; axis < 3; axis++)
            for (var e = 0; e < 4; e++)
            {
                var size = new Vector3(w);
                size[axis] = h[axis] * 2 + w;
                var at = c;
                var a1 = (axis + 1) % 3;
                var a2 = (axis + 2) % 3;
                at[a1] += (e & 1) == 0 ? -h[a1] : h[a1];
                at[a2] += (e & 2) == 0 ? -h[a2] : h[a2];
                gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * m, colour, opacity, mode: 2);
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
