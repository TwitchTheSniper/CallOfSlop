using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Shell Gantry's five arms (ConstructGantry.cs; mining-shell.md "Shell Gantry layout"): each from its shoulder on the rear frame, an
/// upper link, an elbow, a forearm and a gripper, at rest hung forward of the frame; the arm of a swap under way on its path from the
/// job as this peer heard it, carrying the module's part at true size (the old one off the shell, then the new one off the table; the
/// old one lies on the table from when it is set down until the fit, when the table holds it). The same pose on host and client, from
/// the job's clock; the arm's gripper shuts on a module, its nut runner turns while it unbolts or bolts. The waist clamp (a ram from the
/// rear frame, a yoke and two jaws) closes round the shell's hips while a change runs (ConstructGantry.Hold). Drawn within
/// <see cref="DrawNear"/>. Allocates nothing.
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
                for (var a = 0; a < ConstructGantry.ArmCount; a++)
                {
                    var k = world.GantryJobCount > 0 ? ConstructGantry.ArmJob(world, c.Id, part, a, now) : -1;
                    if (k >= 0 && now - world.GantryJobs[k].Start < ConstructGantry.Duration + 1)
                        DrawJob(gfx, world, c, part, gd, frame, world.GantryJobs[k], now);
                    else
                        DrawArm(gfx, gd, frame, a, ConstructGantry.RestOf(gd, a), 0, 0);
                }
                DrawClamp(gfx, world, c, part, gd, pos, rot, frame, now);
            }
        }
    }

    private static void DrawJob(Renderer gfx, ConstructWorld world, Construct c, int part, ShellGantryDef gd, Matrix4x4 frame, in GantryJob j, double now)
    {
        var t = now - j.Start;
        Vector3 slot, away;
        if (world.Get(j.Shell) is { PartCount: 1 } shell)
        {
            slot = ConstructGantry.ToPart(c, part, ConstructGantry.SlotPoint(shell, j.Slot));
            Freeform.PartWorld(shell, 0, out var sp, out var sr);
            var scale = ShellFrames.Of(ShellFrames.FrameOf(shell.Parts[0].D)).Scale;
            away = ConstructGantry.ToPart(c, part, sp + Vector3.Transform(ConstructGantry.AwayOf(j.Slot) * scale, sr)) - ConstructGantry.ToPart(c, part, sp);
        }
        else
            (slot, away) = ((gd.BayMin + gd.BayMax) * 0.5f, new Vector3(0, 0, 0.3f));
        var table = world.Get(j.TableBody) is { } tb && j.Table < tb.PartCount && tb.Parts[j.Table].D.GantryTable ? ConstructGantry.TablePoint(c, part, tb, j.Table) : gd.Arms[j.Arm] + new Vector3(0, -1.2f, 1.4f);
        var hand = ConstructGantry.Hand(gd, j.Arm, slot, away, table, t, out var carry, out var grip);
        var bolting = ConstructGantry.Bolting(t);
        DrawArm(gfx, gd, frame, j.Arm, hand, grip, bolting ? (float)(now * 14 % (Math.PI * 2)) : 0);
        var all = ShellFrames.Modules;
        if (carry is 1 or 2 or 3 && j.Old > 0 && j.Old - 1 < all.Length)
            DrawModule(gfx, all[j.Old - 1], frame, carry == 1 ? hand : ConstructGantry.SetDownOf(table));
        if (carry == 3 && j.New > 0 && j.New - 1 < all.Length)
            DrawModule(gfx, all[j.New - 1], frame, hand);
        if (bolting)
        {
            // (a work lamp at the hand while it unbolts or bolts: what tells a player the arm is busy at the shell)
            var flick = (float)(0.5 + 0.5 * Math.Sin(now * 18));
            gfx.Draw(Matrix4x4.CreateScale(0.08f) * Matrix4x4.CreateTranslation(hand + new Vector3(0, 0.12f, 0.1f)) * frame, new Vector3(1f, 0.6f + 0.3f * flick, 0.2f));
        }
    }

    // The waist clamp (sizes in a person-sized shell's metres, times its frame's scale; mining-shell.md "Swap animation"): a ram from a
    // housing on the rear frame's lower rail out to a yoke behind the shell's hips, and two jaws on the yoke's ends that swing in round the
    // pelvis, a tooth at each jaw's front over the pelvis's front corners. Faces stand 3 mm or more off the shell's (z-fighting.md).
    private const float YokeY = 0.88f, YokeZ = 0.124f, JawX = 0.22f, JawOpen = 1.31f; // (75 degrees out)
    private static readonly Vector3 Housing = new(0.3f, 0.26f, 0.23f), Lamp = new(0.06f, 0.06f, 0.03f);

    /// <summary>The clamp of a gantry: at rest folded at its housing; while a change runs, out on the shell (closing, shut, opening).</summary>
    private static void DrawClamp(Renderer gfx, ConstructWorld world, Construct c, int part, ShellGantryDef gd, Vector3 pos, Quaternion rot, Matrix4x4 frame, double now)
    {
        var mount = new Vector3(0, gd.Arms[1].Y, gd.Arms[1].Z); // (on the lower rail, between the lower arms' shoulders)
        Box(gfx, frame, mount, Housing, Joint);
        var restLocal = mount + new Vector3(0, -0.12f, -0.45f);
        var k = world.GantryLockCount > 0 ? ConstructGantry.LockAt(world, c.Id, part) : -1;
        var shut = 0f;
        Vector3 yoke;
        Quaternion yr;
        var size = 1f;
        if (k >= 0 && world.Get(world.GantryLocks[k].Shell) is { PartCount: 1 } shell && !ConstructGantry.LockDone(world.GantryLocks[k], now))
        {
            shut = ConstructGantry.Shut(world.GantryLocks[k], now);
            Freeform.PartWorld(shell, 0, out var sp, out var sr);
            var scale = ShellFrames.Of(ShellFrames.FrameOf(shell.Parts[0].D)).Scale;
            var reach = Smooth(shut / 0.6f); // (the ram runs out, then the jaws swing in)
            var on = sp + Vector3.Transform(new Vector3(0, YokeY, YokeZ) * scale, sr);
            yoke = Vector3.Lerp(pos + Vector3.Transform(restLocal, rot), on, reach);
            yr = Quaternion.Slerp(rot, sr, reach);
            size = float.Lerp(1, scale, reach);
        }
        else
        {
            yoke = pos + Vector3.Transform(restLocal, rot);
            yr = rot;
        }
        var jaw = Smooth((shut - 0.6f) / 0.4f);
        Box(gfx, frame, mount + new Vector3(0, 0.07f, -Housing.Z * 0.5f - 0.004f), Lamp, jaw > 0.99f ? new Vector3(0.95f, 0.2f, 0.12f) : new Vector3(0.3f, 0.85f, 0.35f)); // (red: shut on a shell)
        // The ram: a sleeve from the housing, the rod out of it to the yoke's back.
        var from = pos + Vector3.Transform(mount + new Vector3(0, -0.02f, -Housing.Z * 0.5f), rot);
        var to = yoke + Vector3.Transform(new Vector3(0, -0.03f, 0.03f) * size, yr);
        Beam(gfx, Matrix4x4.Identity, from, Vector3.Lerp(from, to, 0.45f), 0.11f, Joint);
        Beam(gfx, Matrix4x4.Identity, Vector3.Lerp(from, to, 0.4f), to, 0.065f, Pin);
        var yf = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(yr) * Matrix4x4.CreateTranslation(yoke);
        Box(gfx, yf, Vector3.Zero, new Vector3(0.5f, 0.032f, 0.04f), Joint); // the yoke
        for (var side = -1; side <= 1; side += 2)
        {
            // A jaw: a bar along the pelvis's side from the yoke to past its front, and its tooth; turned out about its end on the yoke.
            var pivot = new Vector3(side * JawX, 0, 0);
            var turn = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationY(-side * JawOpen * (1 - jaw)) * Matrix4x4.CreateTranslation(pivot) * yf;
            Box(gfx, turn, new Vector3(side * JawX, 0, -0.1185f), new Vector3(0.035f, 0.026f, 0.261f), Grip);
            Box(gfx, turn, new Vector3(side * 0.1795f, 0, -0.247f), new Vector3(0.109f, 0.022f, 0.018f), Grip);
        }
    }

    private static float Smooth(float s)
    {
        s = Math.Clamp(s, 0, 1);
        return s * s * (3 - 2 * s);
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

    /// <summary>
    /// One arm: a shoulder block on the rear frame, two links by <see cref="ConstructGantry.Elbow"/>, a wrist and a gripper at the hand, its
    /// fingers shut by <paramref name="grip"/> (0 open, 1 on a module) and its nut runner turned by <paramref name="spin"/> (radians).
    /// </summary>
    private static void DrawArm(Renderer gfx, ShellGantryDef gd, Matrix4x4 frame, int arm, Vector3 hand, float grip, float spin)
    {
        var s = gd.Arms[arm];
        var elbow = ConstructGantry.Elbow(gd, s, hand);
        Box(gfx, frame, s, new Vector3(0.26f, 0.26f, 0.26f), Joint);
        Box(gfx, frame, s, new Vector3(0.32f, 0.1f, 0.1f), Pin);
        Beam(gfx, frame, s, elbow, 0.13f, Link);
        Box(gfx, frame, elbow, new Vector3(0.18f, 0.18f, 0.18f), Joint);
        Beam(gfx, frame, elbow, hand + Vector3.UnitY * 0.12f, 0.1f, Link);
        var wrist = hand + Vector3.UnitY * 0.12f;
        Box(gfx, frame, wrist, new Vector3(0.14f, 0.12f, 0.14f), Joint);
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.05f, 0.1f) * Matrix4x4.CreateRotationZ(spin) * Matrix4x4.CreateTranslation(wrist + new Vector3(0, 0, -0.115f)) * frame, Pin); // (the nut runner)
        var x = 0.13f - 0.06f * grip;
        Box(gfx, frame, hand + new Vector3(-x, 0.02f, 0), new Vector3(0.03f, 0.16f, 0.1f), Grip);
        Box(gfx, frame, hand + new Vector3(x, 0.02f, 0), new Vector3(0.03f, 0.16f, 0.1f), Grip);
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
