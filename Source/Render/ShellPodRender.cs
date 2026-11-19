using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Shell pods' moving parts (ConstructShellPod.cs; docs/sandbox/design/shell-pod.md): the lid, which rises on two posts as the frame
/// (the button panel on its right post), and the platform the shell stands on, from the pod's floor up to the lid. An empty pod's
/// frame rises as a worn shell it takes comes near (fully within half of <see cref="ConstructShellPod.NearRise"/>), from the wearers
/// every peer knows (<see cref="Gather"/>); a job draws the frame, the platform and the shell folding in or out (<see cref="ShellFold"/>)
/// on its clock, the same on host and client. The shell is drawn box by box from its part, each limb turned about its joint.
/// Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </summary>
public static class ShellPodRender
{
    public const float DrawNear = 80f;
    private static readonly Vector3 Cap = new(0.13f, 0.15f, 0.17f), Post = new(0.34f, 0.38f, 0.42f), Plate = new(0.2f, 0.22f, 0.25f),
        Hazard = new(0.95f, 0.69f, 0.12f), Button = new(0.82f, 0.16f, 0.12f), Lamp = new(0.22f, 0.82f, 0.9f),
        LampEmpty = new(0.3f, 0.92f, 0.38f), LampBusy = new(1f, 0.62f, 0.12f), LampBroken = new(0.95f, 0.18f, 0.12f);

    private static readonly (Vector3 At, byte Frame)[] _wearers = new (Vector3, byte)[64];
    private static int _wearerCount;

    /// <summary>Each frame, before drawing: the players in shells this peer knows of (their feet and frame).</summary>
    public static void Gather(Game g)
    {
        _wearerCount = 0;
        foreach (var p in g.Players.Values)
            if (p.InShell && _wearerCount < _wearers.Length)
                _wearers[_wearerCount++] = (p.Position, p.ShellFrame);
    }

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructConveyor.Clock;
        foreach (var c in world.Bodies)
        {
            if (c.PodCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var q = 0; q < c.PodCount; q++)
            {
                int part = c.Pods[q];
                if (part >= c.PartCount || c.DefOf(part).ShellPod is not { } pd)
                    continue;
                DrawPod(gfx, world, c, part, pd, now);
            }
        }
    }

    private static void DrawPod(Renderer gfx, ConstructWorld world, Construct c, int part, ShellPodDef pd, double now)
    {
        ref var pp = ref c.Parts[part];
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var m = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var k = ConstructShellPod.JobAt(world, c.Id, part);
        float extend = 0, lift = 0, fold = 1, slide = 1;
        var frame = (byte)0;
        var shell = false;
        if (k >= 0)
        {
            ref var j = ref world.PodJobs[k];
            ConstructShellPod.Pose(j.Phase, now - j.Start, out extend, out lift, out fold, out slide);
            frame = j.Frame;
            shell = true;
        }
        else if (!pp.Broken && !ConstructShellPod.Holds(pp))
        {
            // An empty pod: its frame rises as a worn shell it takes comes near.
            var best = float.MaxValue;
            for (var w = 0; w < _wearerCount; w++)
            {
                var (at, fr) = _wearers[w];
                if (!ConstructShellPod.Fits(pd, fr) || Vector3.DistanceSquared(at, pos) > 400)
                    continue;
                var feet = ConstructShellPod.StandFeet(c, part, fr, out _, out _);
                var d = Vector3.Distance(at, feet);
                if (d < best)
                    (best, frame) = (d, fr);
            }
            extend = lift = Math.Clamp((ConstructShellPod.NearRise - best) / (ConstructShellPod.NearRise * 0.5f), 0, 1);
        }
        ConstructShellPod.StandFeet(c, part, frame, out var fwd, out var length);
        var bot = pd.Top - pd.Depth;
        var inner = pd.Half - 0.1f;
        // A flush pod's lid is two flaps inside its rim, their tops at its solid's top (walked on): they swing down against the
        // pocket's walls first (FlapShare of the frame's rise), then the frame's head comes up out of the pocket between its two
        // posts. A bolted-on pod's lid is the head itself.
        var open = 1f;
        var head = extend;
        var capBase = pd.Top - 0.002f;
        var capHalf = inner - 0.005f;
        if (pd.Flush)
        {
            open = Math.Clamp(extend / FlapShare, 0, 1);
            head = Math.Clamp((extend - FlapShare) / (1 - FlapShare), 0, 1);
            capBase = pd.Top - 0.1f;
            capHalf = pd.Half - 0.198f; // (3 mm inside the posts, which flank it)
            Flaps(gfx, m, pd, open);
        }
        // The head: closed it sits 2 mm under the rim's top (a flush pod's waits under its flaps); out, it tops the frame.
        var capTop = capBase + head * (pd.Top - 0.002f + length - capBase);
        if (!pd.Flush || open >= 1f)
        {
            Box(gfx, m, new Vector3(0, capTop - 0.03f, 0), new Vector3(capHalf * 2 - 0.01f, 0.06f, capHalf * 2 - 0.01f), Cap);
            Box(gfx, m, new Vector3(0, capTop + 0.002f, -capHalf + 0.06f), new Vector3(capHalf * 2 - 0.12f, 0.006f, 0.05f), Hazard);
        }
        if (extend > 0.01f && (!pd.Flush || open >= 1f))
        {
            var px = pd.Half - 0.16f;
            var y0 = pd.Top - 0.06f;
            var y1 = pd.Flush ? capTop - 0.002f : capTop - 0.06f; // (a flush pod's posts run up beside its head: it reads as held)
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
                Box(gfx, m, new Vector3(s * px, (y0 + y1) * 0.5f, 0), new Vector3(0.08f, MathF.Max(0.01f, y1 - y0), 0.08f), Post);
            // The button panel on the right post, at a hand's height in the frame, and a lamp on each post's head.
            var by = MathF.Min(y1 - 0.1f, pd.Top + MathF.Min(length * 0.55f, 1.1f));
            Box(gfx, m, new Vector3(px - 0.07f, by, -0.06f), new Vector3(0.06f, 0.16f, 0.12f), Post);
            Box(gfx, m, new Vector3(px - 0.105f, by, -0.06f), new Vector3(0.012f, 0.06f, 0.06f), Button);
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
                Box(gfx, m, new Vector3(s * px, y1 - 0.04f, -0.045f), new Vector3(0.05f, 0.03f, 0.012f), Lamp);
        }
        // The status lamp on the rim's corner: green empty, amber with a shell in it or at work, red broken.
        var rimTop = ConstructShellPod.RimTop(pd);
        var lampAt = pd.Half - 0.03f; // (on the rim's outer half, clear of the trim along its inner edge)
        var status = pp.Broken ? LampBroken : shell || ConstructShellPod.Holds(pp) ? LampBusy : LampEmpty;
        gfx.Draw(Matrix4x4.CreateScale(0.066f, 0.01f, 0.066f) * Matrix4x4.CreateTranslation(lampAt, rimTop + 0.003f, -lampAt) * m, Cap); // its dark bezel, so it reads on the hazard paint
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.014f, 0.05f) * Matrix4x4.CreateTranslation(lampAt, rimTop + 0.011f, -lampAt) * m, status, mode: 2); // a glowing lens, 5 cm
        // The platform: from the pod's floor up to the lid.
        var plat = bot + 0.08f + lift * (pd.Top - 0.012f - (bot + 0.08f));
        if (lift > 0.01f || shell)
            gfx.Draw(Matrix4x4.CreateScale(inner * 2 - 0.06f, 0.04f, inner * 2 - 0.06f) * Matrix4x4.CreateTranslation(0, plat - 0.02f, 0) * m, Plate,
                surface: Surfaces.Code(Surfaces.Id("tread plate"), 0f)); // (dark tread plate: the pocket reads deep)
        if (!shell || ConstructParts.Find(ShellFrames.Of(frame).Part) is not { } sd)
            return;
        DrawShell(gfx, world.PodJobs[k], sd, ShellFrames.Of(frame), c, part, m, rot, pos, plat, fold, slide, fwd);
    }

    /// <summary>The shell between its standing pose (in the frame, or where it stood) and its folded pose on the platform.</summary>
    private static void DrawShell(Renderer gfx, in PodJob j, ConstructPartDef sd, ShellFrame fr, Construct c, int part, Matrix4x4 pod, Quaternion rot, Vector3 pos, float plat, float fold, float slide, Vector3 fwd)
    {
        var pd = c.DefOf(part).ShellPod!;
        ConstructShellPod.Lid(c, part, pd, out _, out _, out var mount, out var up, out _);
        var k = fr.Scale;
        // Folded: on the platform in the pod's frame; a ceiling pod hangs it by its top, so it is turned over.
        var flip = mount == 2;
        var local = flip ? new Vector3(0, plat + fr.Fold.Height * k, 0) : new Vector3(0, plat, 0);
        var qf = flip ? Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI), rot) : rot;
        var pf = pos + Vector3.Transform(local, rot);
        // Standing: in the frame (a dock slides it there from where it stood).
        var ps = ConstructShellPod.StandFeet(c, part, j.Frame, out _, out _);
        var qs = Upright(up, fwd);
        if (j.Phase == ConstructShellPod.Dock && j.From != Vector3.Zero && slide < 1)
        {
            var f0 = new Vector3(MathF.Sin(j.Yaw), 0, -MathF.Cos(j.Yaw));
            ps = Vector3.Lerp(j.From, ps, slide);
            qs = Quaternion.Slerp(Upright(up, f0), qs, slide);
        }
        var q = Quaternion.Slerp(qs, qf, fold);
        var place = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(Vector3.Lerp(ps, pf, fold));
        var fd = fr.Fold;
        for (var bx = 0; bx < sd.Boxes.Length; bx++)
        {
            ref readonly var box = ref sd.Boxes[bx];
            if (!box.Hidden)
                gfx.Draw(box.Model(0, box.Center) * Limb(fd, k, box.Center, fold) * place, box.Color, shape: box.Shape);
        }
    }

    /// <summary>The share of a flush pod's frame rise its lid's flaps take to swing open.</summary>
    public const float FlapShare = 0.3f;

    /// <summary>The top of a flush pod's closed flaps: 2 mm under its solid's top (where a foot stands), 5 mm under its rim.</summary>
    private static float FlapTop(ShellPodDef pd) => pd.Top - 0.002f;

    /// <summary>
    /// A flush pod's lid: two flaps meeting in the middle, hinged at their outer edges just inside the walls' lining; opened
    /// (<paramref name="open"/> 1) they hang straight down against it, clear of the platform rising between them.
    /// </summary>
    private static void Flaps(Renderer gfx, in Matrix4x4 m, ShellPodDef pd, float open)
    {
        const float Thick = 0.03f;
        var hx = pd.Half - ConstructShellPod.Wall - 0.016f; // (the lining's face is 12 mm in from the wall's: 4 mm clear)
        var len = hx - 0.002f; // (4 mm apart in the middle)
        var zh = hx - 0.004f;
        var top = FlapTop(pd);
        var a = open * open * (3 - 2 * open) * MathF.PI * 0.5f;
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
        {
            var hinge = Matrix4x4.CreateRotationZ(s * a) * Matrix4x4.CreateTranslation(s * hx, top - Thick, 0) * m; // (hinged at the lower outer edge: open, a flap hangs inside it)
            gfx.Draw(Matrix4x4.CreateScale(len, Thick, zh * 2) * Matrix4x4.CreateTranslation(-s * len * 0.5f, Thick * 0.5f, 0) * hinge, Cap);
            // A hazard band just inside the rim along each flap's hinge edge (the pair reads as one hatch with a border, not a
            // double door), and a grip slot by the edge where they meet.
            gfx.Draw(Matrix4x4.CreateScale(0.035f, 0.006f, zh * 2 - 0.1f) * Matrix4x4.CreateTranslation(-s * 0.045f, Thick + 0.002f, 0) * hinge, Hazard);
            gfx.Draw(Matrix4x4.CreateScale(0.03f, 0.006f, MathF.Min(0.24f, zh)) * Matrix4x4.CreateTranslation(-s * (len - 0.05f), Thick + 0.002f, 0) * hinge, Post);
        }
    }

    /// <summary>A rotation whose +y is <paramref name="up"/> and -z is <paramref name="fwd"/> (made square to up).</summary>
    private static Quaternion Upright(Vector3 up, Vector3 fwd)
    {
        var f = fwd - up * Vector3.Dot(fwd, up);
        f = f.LengthSquared() < 1e-6f ? Vector3.Cross(up, Vector3.UnitX) : Vector3.Normalize(f);
        var x = Vector3.Cross(f, up);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, -f.X, -f.Y, -f.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    /// <summary>
    /// Where a box of the shell goes at fold <paramref name="f"/>, by the limb its middle is in (a robot of the part kit's layout at
    /// scale <paramref name="k"/>): shins turn at the knee and thighs at the hip, the body bends at the hip, arms turn at the shoulder
    /// and elbow with it, the head sinks; then the whole comes down and back.
    /// </summary>
    private static Matrix4x4 Limb(ShellFold fd, float k, Vector3 centre, float f)
    {
        if (f <= 0)
            return Matrix4x4.Identity;
        const float Deg = MathF.PI / 180f;
        var u = centre / k;
        var ax = MathF.Abs(u.X);
        var hip = About(0.88f * k, fd.Hip * Deg * f);
        var torso = About(0.88f * k, fd.Bend * Deg * f);
        Matrix4x4 limb;
        if (ax >= 0.25f && u.Y >= 0.55f && u.Y < 1.6f)
        {
            var left = u.X < 0;
            var upper = About(1.45f * k, (left ? fd.LeftShoulder : fd.RightShoulder) * Deg * f) * torso;
            limb = u.Y >= 1.0f ? upper : About(1.0f * k, (left ? fd.LeftElbow : fd.RightElbow) * Deg * f) * upper;
        }
        else if (u.Y < 0.53f)
            limb = About(0.5f * k, fd.Knee * Deg * f) * hip;
        else if (u.Y < 0.86f)
            limb = hip;
        else if (u.Y < 1.0f && u.Z < 0.12f)
            limb = Matrix4x4.Identity; // (the pelvis)
        else if (u.Y >= 1.47f && u.Z < 0.12f && ax < 0.2f)
            limb = Matrix4x4.CreateTranslation(0, -fd.Head * k * f, 0) * torso;
        else
            limb = torso;
        return limb * Matrix4x4.CreateTranslation(0, -fd.Drop * k * f, fd.Shift * k * f);
    }

    /// <summary>A turn about the x axis through height <paramref name="y"/> (positive: up towards the back, down towards the front).</summary>
    private static Matrix4x4 About(float y, float angle) =>
        Matrix4x4.CreateTranslation(0, -y, 0) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(0, y, 0);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 size, Vector3 colour) =>
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * frame, colour);
}
