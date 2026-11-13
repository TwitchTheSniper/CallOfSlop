using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters' plain drawing (phase 1 of docs/sandbox/design/arms-and-cranes.md): the boom as one box from the shoulder to the hand,
/// a gripper over the piece, and the piece at true size in its paint on the move's path (ConstructLifters.Pose), run on from the
/// last word the peer heard. Nothing is drawn for a lifter more than <see cref="DrawNear"/> away. Allocates nothing.
/// </summary>
public static class LifterRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Boom = new(0.48f, 0.27f, 0.2f), Grip = new(0.94f, 0.47f, 0.09f);
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
                if (l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } ld || c.Parts[l.Part].Broken)
                    continue;
                ConstructLifters.Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
                if (Vector3.DistanceSquared(eye, shoulder) > DrawNear * DrawNear)
                    continue;
                Vector3 origin;
                var turn = Quaternion.Identity;
                if (l.Moved)
                    origin = ConstructLifters.Pose(ld, l, ConstructLifters.AgeNow(l), out turn);
                else
                    origin = ld.Base + new Vector3(ld.ReachMin + 0.6f, -0.6f, 0); // (at rest before its first move)
                var worldRot = Quaternion.Normalize(lr * turn);
                var at = lp + Vector3.Transform(origin, lr);
                var grip = at;
                var span = 0.35f;
                if (l.Def != 0 && ConstructParts.Get(l.Def) is { } d)
                {
                    var m = Matrix4x4.CreateFromQuaternion(worldRot) * Matrix4x4.CreateTranslation(at);
                    var paint = l.Paint != 0 ? ConstructPaint.ToVector(l.Paint) : Vector3.Zero;
                    for (var bx = 0; bx < d.Boxes.Length; bx++)
                    {
                        ref readonly var box = ref d.Boxes[bx];
                        if (!box.Hidden)
                            gfx.Draw(box.Model(0, box.Center) * m, l.Paint != 0 && box.Paintable ? paint : box.Color, shape: box.Shape);
                    }
                    var top = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
                    grip = at + Vector3.Transform(top, worldRot) + Vector3.UnitY * ConstructLifters.GripLift;
                    span = Math.Clamp(MathF.Min(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z) * 0.8f, 0.2f, 1.2f);
                }
                // The gripper: a bar over the piece with two pads, under a short wrist the boom ends in.
                var up = Vector3.Transform(Vector3.UnitY, lr);
                var wrist = grip + up * 0.3f;
                var gm = Matrix4x4.CreateFromQuaternion(lr) * Matrix4x4.CreateTranslation(grip + up * 0.04f);
                gfx.Draw(Matrix4x4.CreateScale(span + 0.1f, 0.08f, 0.14f) * gm, Grip);
                gfx.Draw(Matrix4x4.CreateScale(0.1f, 0.5f, 0.1f) * Matrix4x4.CreateTranslation(0, 0.25f, 0) * Matrix4x4.CreateTranslation(-0.004f, 0, 0) * gm, Boom);
                Beam(gfx, shoulder, wrist, 0.16f, Boom);
            }
        }
    }

    /// <summary>A square beam from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Beam(Renderer gfx, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z);
        x = Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f), colour);
    }
}
