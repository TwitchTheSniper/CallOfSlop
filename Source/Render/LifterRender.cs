using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters' drawing (docs/sandbox/design/arms-and-cranes.md, section 6): an arm's turning half, posed each frame from its hand by
/// <see cref="ArmIk.Solve"/> (a turret on the part's slewing ring, a shoulder, the upper arm, an elbow, the forearm, a wrist and a
/// gripper), and the piece at true size in its paint on the move's path (ConstructLifters.Pose), run on from the last word the
/// peer heard. The pose comes from the hand alone, so host and client draw the same arm. Nothing is drawn for a lifter more than
/// <see cref="DrawNear"/> away. Allocates nothing.
/// </summary>
public static class LifterRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Out = new(0.94f, 0.47f, 0.09f), Accent = new(0.95f, 0.69f, 0.12f);
    private static double _scanAt;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var now = ConstructConveyor.Clock;
        if (world.LiftBusy == 0 && now - _scanAt > 1.0)
        {
            _scanAt = now; // (a client finds its lifters itself; a host's own step looks while a hand is busy)
            ConstructLifters.Scan(world, server: false);
        }
        var eye = gfx.CameraPosition;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Part >= c.PartCount || ConstructLifters.DefOf(c, l.Part) is not { } ld || c.Parts[l.Part].Broken)
                    continue;
                ConstructLifters.Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
                // The hand, in the part's frame: the piece's origin and turn on the path, the gripper bar on its top.
                var turn = Quaternion.Identity;
                var origin = l.Moved ? ConstructLifters.Pose(ld, l, ConstructLifters.AgeNow(l), out turn) : ConstructLifters.RestOf(ld); // (at rest before its first move)
                // Culled by the nearer of its base and its hand: a crane's bridge and piece may be 300 m along its rails from its cabinet.
                var handAt = lp + Vector3.Transform(origin, lr);
                if (Vector3.DistanceSquared(eye, shoulder) > DrawNear * DrawNear && Vector3.DistanceSquared(eye, handAt) > DrawNear * DrawNear)
                    continue;
                if (_lookBody == c.Id && _lookPart == l.Part && now - _lookAt < 0.3 && ld.Bridge)
                    DrawCraneReach(gfx, ld, Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp));
                else if (_lookBody == c.Id && _lookPart == l.Part && now - _lookAt < 0.3)
                    DrawReach(gfx, world, c, l.Part, ld, Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp), shoulder);
                var grip = origin + Vector3.UnitY * ConstructLifters.GripLift;
                var span = 0.35f;
                var part = Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp);
                if (l.Def != 0 && ConstructParts.Get(l.Def) is { } d)
                {
                    var m = Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(origin) * part;
                    var paint = l.Paint != 0 ? ConstructPaint.ToVector(l.Paint) : Vector3.Zero;
                    for (var bx = 0; bx < d.Boxes.Length; bx++)
                    {
                        ref readonly var box = ref d.Boxes[bx];
                        if (!box.Hidden)
                            gfx.Draw(box.Model(0, box.Center) * m, l.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
                    }
                    var top = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
                    grip = origin + Vector3.Transform(top, turn) + Vector3.UnitY * ConstructLifters.GripLift;
                    span = Math.Clamp(MathF.Min(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z) * 0.8f, 0.25f, 1.2f);
                }
                if (ld.Bridge)
                {
                    DrawCrane(gfx, ld, part, grip);
                    continue;
                }
                ArmIk.Solve(ld, grip, out var yaw, out var elbow, out var wrist);
                DrawArm(gfx, ld, part, yaw, elbow, wrist, span);
            }
        }
    }

    private static ushort _lookBody;
    private static int _lookPart = -1;
    private static double _lookAt = -10;
    private const int RingSegments = 40;

    /// <summary>A lifter looked at this frame: its reach is drawn round it (Program.Sandbox.cs calls this each frame it is aimed at).</summary>
    public static void Look(ushort body, int part) => (_lookBody, _lookPart, _lookAt) = (body, part, ConstructConveyor.Clock);

    /// <summary>
    /// The reach view (arms-and-cranes.md section 6): an arm's ring in amber at its plinth's floor and at the top of its reach, its
    /// two radii, and a post at each end of the ring's quarters; a Building Station building in reach shows its hold point green.
    /// </summary>
    private static void DrawReach(Renderer gfx, ConstructWorld world, Construct c, int part, LifterDef ld, Matrix4x4 frame, Vector3 shoulder)
    {
        var amber = new Vector3(1f, 0.72f, 0.15f);
        var floorY = ld.Base.Y - ld.Down;
        const float t = 0.04f;
        for (var ring = 0; ring < 2; ring++)
        {
            var r = ring == 0 ? ld.ReachMin : ld.ReachMax;
            for (var level = 0; level < 2; level++)
            {
                var y = level == 0 ? floorY : ld.Base.Y + ld.Up;
                var chord = 2 * r * MathF.Sin(MathF.PI / RingSegments) + t;
                for (var k = 0; k < RingSegments; k++)
                {
                    var a = (k + 0.5f) * MathF.Tau / RingSegments;
                    var at = new Vector3(ld.Base.X + r * MathF.Cos(a), y, ld.Base.Z + r * MathF.Sin(a));
                    var m = Matrix4x4.CreateScale(t, t, chord) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(at) * frame;
                    gfx.Draw(m, amber, 0.8f);
                }
            }
            for (var q = 0; q < 4; q++)
            {
                var a = q * MathF.PI * 0.5f;
                var h = ld.Up + ld.Down;
                var at = new Vector3(ld.Base.X + r * MathF.Cos(a), floorY + h * 0.5f, ld.Base.Z + r * MathF.Sin(a));
                gfx.Draw(Matrix4x4.CreateScale(t, h, t) * Matrix4x4.CreateTranslation(at) * frame, amber, 0.8f);
            }
        }
        // The stations it serves: a green post over each building station's hold point it reaches, red when the hold is out of reach.
        for (var si = 0; si < world.StationOpen.Count; si++)
        {
            if (world.Get(world.StationOpen[si]) is not { } sc)
                continue;
            for (var st = 0; st < sc.PartCount; st++)
            {
                if (!sc.DefOf(st).IsBuildStation || sc.Parts[st].Station is not { State: StationState.Building })
                    continue;
                if (Vector3.Distance(ConstructLifters.HoldPoint(sc, st, shoulder), shoulder) > ld.ReachMax * 2 + 4)
                    continue;
                Freeform.PartWorld(c, part, out _, out var lr);
                var ok = ConstructLifters.HoldPick(sc, st, ld, shoulder, lr, ConstructLifters.SafeLift, ConstructLifters.SafeLift, out var hold); // (the nearest point along the hold's side it reaches)
                gfx.Draw(Matrix4x4.CreateScale(0.08f, 1.2f, 0.08f) * Matrix4x4.CreateTranslation(hold + Vector3.UnitY * 0.6f), ok ? new Vector3(0.3f, 1f, 0.45f) : new Vector3(1f, 0.25f, 0.2f), 0.8f);
            }
        }
    }

    /// <summary>The turning half of an arm, from the part's frame matrix and its pose (all in the part's frame), in its tier's look and size.</summary>
    private static void DrawArm(Renderer gfx, LifterDef ld, Matrix4x4 part, float yaw, Vector3 elbow, Vector3 wrist, float span)
    {
        var s = ld.Base;
        var k = ld.Upper / 2.3f; // (the tier's size: the drawn half grows with its links)
        var heading = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var side = new Vector3(-heading.Z, 0, heading.X);
        var turret = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw)); // (+x towards the hand)
        // The turret: a squat housing on the slewing ring (sunk 4 mm into it), a counterweight behind, the shoulder's yoke on top.
        var floor = ld.Ring - 0.004f;
        var h = s.Y - 0.12f * k - floor;
        Box(gfx, part, turret, new Vector3(s.X, floor + h * 0.5f, s.Z), new Vector3(0.72f, h / k, 0.66f) * k, ld.Turret);
        var weight = s - heading * 0.5f * k + new Vector3(0, floor + 0.3f * k - s.Y, 0);
        Box(gfx, part, turret, weight, new Vector3(0.36f, 0.42f, 0.58f) * k, ld.Weight);
        Box(gfx, part, turret, weight + side * 0.292f * k, new Vector3(0.3f, 0.08f, 0.012f) * k, Accent); // (hazard on the weight's flank)
        Box(gfx, part, turret, s + side * 0.22f * k, new Vector3(0.3f, 0.34f, 0.1f) * k, ld.Joint);
        Box(gfx, part, turret, s - side * 0.22f * k, new Vector3(0.3f, 0.34f, 0.1f) * k, ld.Joint);
        Box(gfx, part, turret, s, new Vector3(0.24f, 0.24f, 0.56f) * k, ld.Pin); // (the shoulder's pin: what moves)
        // The upper arm, the elbow, the forearm.
        Beam(gfx, part, s, elbow, 0.2f * k, ld.UpperCol);
        if (ld.Tier >= 3)
            Beam(gfx, part, s + (elbow - s) * 0.15f + Vector3.UnitY * 0.16f * k, s + (elbow - s) * 0.7f + Vector3.UnitY * 0.16f * k, 0.07f * k, ld.Joint); // (a hydraulic ram along the upper arm)
        Box(gfx, part, turret, elbow, new Vector3(0.28f, 0.28f, 0.34f) * k, ld.Joint);
        Box(gfx, part, turret, elbow, new Vector3(0.14f, 0.14f, 0.42f) * k, ld.Pin); // (the elbow's pin)
        Beam(gfx, part, elbow, wrist, 0.15f * k, ld.ForeCol);
        // The wrist: a knuckle, a rod straight down to the gripper bar, the pads either side of the piece.
        Box(gfx, part, turret, wrist, new Vector3(0.18f, 0.18f, 0.22f) * k, ld.Joint);
        var bar = wrist - Vector3.UnitY * ConstructLifters.Wrist;
        Beam(gfx, part, wrist, bar + Vector3.UnitY * 0.04f, 0.07f * MathF.Min(k, 1.5f), ld.Joint);
        Box(gfx, part, turret, bar + Vector3.UnitY * 0.04f, new Vector3(0.14f, 0.08f, span + 0.12f), Out);
        for (var e = -1; e <= 1; e += 2)
            Box(gfx, part, turret, bar + side * (e * (span * 0.5f + 0.03f)) - Vector3.UnitY * 0.1f, new Vector3(0.1f, 0.24f, 0.04f), Out);
    }

    private static readonly Vector3 CraneYellow = new(0.94f, 0.76f, 0.1f), CraneDark = new(0.133f, 0.149f, 0.169f), CraneSteel = new(0.6f, 0.63f, 0.66f);

    /// <summary>
    /// A Bridge Crane's moving half (arms-and-cranes.md 3.4, section 6), in the part's frame from the hook's place: an end truck on each
    /// rail, a double girder across the span over them, the trolley on the girders over the hook, the cable down between the girders
    /// and the hook block with its orange hook on the piece's top. The rails are the parts. Its size follows the hook's depth (the class).
    /// </summary>
    private static void DrawCrane(Renderer gfx, LifterDef ld, Matrix4x4 part, Vector3 grip)
    {
        var k = MathF.Max(0.25f, ld.Depth / 0.6f); // (Medium 1, Large 4, Capital 16)
        var b = ld.Base;
        var hz = grip.Z;
        var id = Matrix4x4.Identity;
        // The end trucks, sat 5 mm over the rails' top; the girders on them; the trolley on the girders.
        for (var e = 0; e < 2; e++)
        {
            var x = b.X + e * ld.Span;
            Box(gfx, part, id, new Vector3(x, b.Y + 0.155f * k, hz), new Vector3(0.6f, 0.3f, 1.8f) * k, CraneDark);
            Box(gfx, part, id, new Vector3(x + (e == 0 ? -0.305f : 0.305f) * k, b.Y + 0.155f * k, hz), new Vector3(0.012f, 0.1f, 1.2f) * k, MachineAccent); // (hazard on its outer face)
        }
        var gl = ld.Span + 0.6f * k;
        for (var s = -1; s <= 1; s += 2)
            Box(gfx, part, id, new Vector3(b.X + ld.Span * 0.5f, b.Y + 0.545f * k, hz + s * 0.4f * k), new Vector3(gl, 0.5f * k, 0.26f * k), CraneYellow);
        var hx = grip.X;
        Box(gfx, part, id, new Vector3(hx, b.Y + 0.999f * k, hz), new Vector3(1.0f, 0.4f, 1.24f) * k, CraneDark);
        Box(gfx, part, id, new Vector3(hx + 0.3f * k, b.Y + 1.25f * k, hz), new Vector3(0.36f, 0.12f, 0.5f) * k, CraneSteel); // (its hoist drum's housing)
        // The cable: two falls between the girders down to the hook block.
        var block = grip + Vector3.UnitY * (0.22f * k);
        for (var s = -1; s <= 1; s += 2)
            Beam(gfx, part, new Vector3(hx, b.Y + 0.8f * k, hz + s * 0.08f * k), block + new Vector3(0, 0.1f * k, s * 0.08f * k), 0.035f * k, CraneDark);
        Box(gfx, part, id, block, new Vector3(0.3f, 0.36f, 0.34f) * k, Out);
        Box(gfx, part, id, block + new Vector3(0.152f * k, 0, 0), new Vector3(0.004f, 0.3f, 0.26f) * k, CraneDark); // (a dark band on the block)
        Beam(gfx, part, block - Vector3.UnitY * 0.18f * k, grip + Vector3.UnitY * 0.01f, 0.06f * k, CraneSteel); // (the hook's shank to the piece)
    }

    private static readonly Vector3 MachineAccent = new(0.95f, 0.69f, 0.12f);

    /// <summary>A crane's reach view: its box's edges in amber, under the rails from the hook's highest point to its drop.</summary>
    private static void DrawCraneReach(Renderer gfx, LifterDef ld, Matrix4x4 frame)
    {
        if (ld.RailWhy != 0)
            return;
        var amber = new Vector3(1f, 0.72f, 0.15f);
        const float t = 0.05f;
        var b = ld.Base;
        float x0 = b.X + ld.AcrossMin, x1 = b.X + ld.AcrossMax, y0 = b.Y - ld.Down, y1 = b.Y - ld.Depth, z0 = b.Z + ld.RunLo, z1 = b.Z + ld.RunHi;
        for (var i = 0; i < 4; i++)
        {
            float y = (i & 1) == 0 ? y0 : y1, z = (i & 2) == 0 ? z0 : z1, x = (i & 2) == 0 ? x0 : x1;
            gfx.Draw(Matrix4x4.CreateScale(x1 - x0, t, t) * Matrix4x4.CreateTranslation((x0 + x1) * 0.5f, y, z) * frame, amber, 0.8f);
            gfx.Draw(Matrix4x4.CreateScale(t, t, z1 - z0) * Matrix4x4.CreateTranslation(x, y, (z0 + z1) * 0.5f) * frame, amber, 0.8f);
            gfx.Draw(Matrix4x4.CreateScale(t, y1 - y0, t) * Matrix4x4.CreateTranslation((i & 1) == 0 ? x0 : x1, (y0 + y1) * 0.5f, z) * frame, amber, 0.8f);
        }
    }

    private static void Box(Renderer gfx, Matrix4x4 part, Matrix4x4 turn, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * turn * Matrix4x4.CreateTranslation(at) * part, colour);

    /// <summary>A square beam from <paramref name="a"/> to <paramref name="b"/>, in the part's frame.</summary>
    private static void Beam(Renderer gfx, Matrix4x4 part, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * part, colour);
    }
}
