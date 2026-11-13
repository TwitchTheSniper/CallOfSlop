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
                if (l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { Bridge: false } ld || c.Parts[l.Part].Broken)
                    continue;
                ConstructLifters.Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
                if (Vector3.DistanceSquared(eye, shoulder) > DrawNear * DrawNear)
                    continue;
                if (_lookBody == c.Id && _lookPart == l.Part && now - _lookAt < 0.3)
                    DrawReach(gfx, world, c, l.Part, ld, Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(lp), shoulder);
                // The hand, in the part's frame: the piece's origin and turn on the path, the gripper bar on its top.
                Vector3 origin;
                var turn = Quaternion.Identity;
                if (l.Moved)
                    origin = ConstructLifters.Pose(ld, l, ConstructLifters.AgeNow(l), out turn);
                else
                    origin = ld.Base + new Vector3(ld.ReachMin + 0.9f, -0.9f, 0); // (at rest before its first move)
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
                var hold = ConstructLifters.HoldPoint(sc, st, shoulder);
                if (Vector3.Distance(hold, shoulder) > ld.ReachMax * 2 + 4)
                    continue;
                Freeform.PartWorld(c, part, out _, out var lr);
                var ok = ConstructLifters.InReach(ld, shoulder, lr, hold, ConstructLifters.SafeLift, out _);
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
