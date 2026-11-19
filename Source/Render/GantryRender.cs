using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Shell Gantry's five arms (ConstructGantry.cs; mining-shell.md "Shell Gantry layout"): each from its shoulder on the rear frame, an
/// upper link, an elbow, a forearm and a gripper, at rest hung forward of the frame; the arm of a swap under way on its path from the
/// job as this peer heard it, carrying the module's part at true size (the old one off the shell, then the new one off the table; the
/// old one lies on the table from when it is set down until the fit, when the table holds it). The same pose on host and client, from
/// the job's clock. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class GantryRender
{
    public const float DrawNear = 120f;
    private static readonly Vector3 Link = new(0.86f, 0.55f, 0.12f), Joint = new(0.2f, 0.22f, 0.25f), Pin = new(0.6f, 0.63f, 0.66f), Grip = new(0.94f, 0.47f, 0.09f);

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructConveyor.Clock;
        foreach (var c in world.Bodies)
        {
            if (c.GantryCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var g = 0; g < c.GantryCount; g++)
            {
                int part = c.Gantries[g];
                if (part >= c.PartCount || c.Parts[part].Broken || c.DefOf(part).Gantry is not { } gd)
                    continue;
                Freeform.PartWorld(c, part, out var pos, out var rot);
                var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
                var k = ConstructGantry.JobAt(world, c.Id, part);
                var busy = k >= 0 && now - world.GantryJobs[k].Start < ConstructGantry.Duration + 1;
                for (var a = 0; a < ConstructGantry.ArmCount; a++)
                {
                    if (busy && world.GantryJobs[k].Arm == a)
                        DrawJob(gfx, world, c, part, gd, frame, world.GantryJobs[k], now);
                    else
                        DrawArm(gfx, gd, frame, a, ConstructGantry.RestOf(gd, a));
                }
            }
        }
    }

    private static void DrawJob(Renderer gfx, ConstructWorld world, Construct c, int part, ShellGantryDef gd, Matrix4x4 frame, in GantryJob j, double now)
    {
        var t = now - j.Start;
        var slot = world.Get(j.Shell) is { PartCount: 1 } shell ? ConstructGantry.ToPart(c, part, ConstructGantry.SlotPoint(shell, j.Slot)) : (gd.BayMin + gd.BayMax) * 0.5f;
        var table = j.Table < c.PartCount && c.Parts[j.Table].D.GantryTable ? ConstructGantry.TablePoint(c, part, j.Table) : gd.Arms[j.Arm] + new Vector3(0, -1.2f, 1.4f);
        var hand = ConstructGantry.Hand(gd, j.Arm, slot, table, t, out var carry);
        DrawArm(gfx, gd, frame, j.Arm, hand);
        var all = ShellFrames.Modules;
        if (carry is 1 or 3 && j.Old > 0 && j.Old - 1 < all.Length)
            DrawModule(gfx, all[j.Old - 1], frame, carry == 1 ? hand : table);
        if (carry == 3 && j.New > 0 && j.New - 1 < all.Length)
            DrawModule(gfx, all[j.New - 1], frame, hand);
        if (t >= ConstructGantry.TAtShell && t < ConstructGantry.TUnclipped || t >= ConstructGantry.TBack && t < ConstructGantry.TFitted)
        {
            // (a work lamp at the hand while it unclips or fits: what tells a player the arm is busy at the shell)
            var flick = (float)(0.5 + 0.5 * Math.Sin(now * 18));
            gfx.Draw(Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(hand + new Vector3(0, 0.12f, 0.1f)) * frame, new Vector3(1f, 0.6f + 0.3f * flick, 0.2f));
        }
    }

    /// <summary>A module's part at true size with its middle at <paramref name="at"/> (the part's frame).</summary>
    private static void DrawModule(Renderer gfx, ShellModule m, Matrix4x4 frame, Vector3 at)
    {
        if (m.Part.Length == 0 || ConstructParts.Find(m.Part) is not { } d)
            return;
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var place = Matrix4x4.CreateTranslation(at - mid - Vector3.UnitY * 0.05f) * frame;
        for (var bx = 0; bx < d.Boxes.Length; bx++)
        {
            ref readonly var box = ref d.Boxes[bx];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * place, box.Color, shape: box.Shape);
        }
    }

    /// <summary>One arm: a shoulder block on the rear frame, two links by <see cref="ConstructGantry.Elbow"/>, a wrist and a gripper at the hand.</summary>
    private static void DrawArm(Renderer gfx, ShellGantryDef gd, Matrix4x4 frame, int arm, Vector3 hand)
    {
        var s = gd.Arms[arm];
        var elbow = ConstructGantry.Elbow(gd, s, hand);
        Box(gfx, frame, s, new Vector3(0.26f, 0.26f, 0.26f), Joint);
        Box(gfx, frame, s, new Vector3(0.32f, 0.1f, 0.1f), Pin);
        Beam(gfx, frame, s, elbow, 0.13f, Link);
        Box(gfx, frame, elbow, new Vector3(0.18f, 0.18f, 0.18f), Joint);
        Beam(gfx, frame, elbow, hand + Vector3.UnitY * 0.12f, 0.1f, Link);
        Box(gfx, frame, hand + Vector3.UnitY * 0.12f, new Vector3(0.14f, 0.12f, 0.14f), Joint);
        Box(gfx, frame, hand + new Vector3(-0.1f, 0.02f, 0), new Vector3(0.03f, 0.16f, 0.1f), Grip);
        Box(gfx, frame, hand + new Vector3(0.1f, 0.02f, 0), new Vector3(0.03f, 0.16f, 0.1f), Grip);
    }

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * frame, colour);

    /// <summary>A square beam from <paramref name="a"/> to <paramref name="b"/>, in the part's frame.</summary>
    private static void Beam(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float thick, Vector3 colour)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-3f)
            return;
        var z = d / len;
        var x = Vector3.Normalize(Vector3.Cross(MathF.Abs(z.Y) > 0.98f ? Vector3.UnitX : Vector3.UnitY, z));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame, colour);
    }
}
