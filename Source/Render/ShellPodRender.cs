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
        Hazard = new(0.95f, 0.69f, 0.12f), Button = new(0.82f, 0.16f, 0.12f),
        StrutCap = new(0.082f, 0.353f, 0.392f), Stage = new(0.55f, 0.58f, 0.62f), TrussMetal = new(0.66f, 0.69f, 0.72f),
        Flange22 = new(0.133f, 0.149f, 0.169f), Bolt = new(0.596f, 0.631f, 0.663f),
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
                DrawPod(gfx, world, c, part, pd, now, q < c.PodSurface.Length ? c.PodSurface[q] : -1f);
            }
        }
    }

    private static void DrawPod(Renderer gfx, ConstructWorld world, Construct c, int part, ShellPodDef pd, double now, float surface)
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
        var px = pd.Half - 0.16f; // the frame's two sides
        if (extend > 0.01f && (!pd.Flush || open >= 1f))
        {
            // Each side: two telescoping pistons, stowed in the pod and drawn out stage by stage, a lighting truss between them;
            // the head rides on their tops (the user, 2026-11-20).
            var bot0 = pd.Top - pd.Depth + ConstructShellPod.Lining + 0.006f;
            var headBottom = (pd.Flush ? capTop - 0.002f : capTop - 0.06f) + 0.003f; // (3 mm into the head: a flush pod's pistons flank it)
            var pz = MathF.Min((pd.Half - 0.1f) * 0.6f, pd.Half - 0.13f);
            var full = (pd.Flush ? pd.Top + length - 0.004f : pd.Top + length - 0.062f) + 0.003f; // (the head's underside risen)
            var top = 0f;
            foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
            {
                foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
                    top = Piston(gfx, m, new Vector3(s * px, 0, sz * pz), pd.Top, bot0, headBottom, full);
                Truss(gfx, m, s * px, top - 0.085f, pz);
                foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
                    Box(gfx, m, new Vector3(s * px, top + 0.004f, sz * pz), new Vector3(0.07f, 0.012f, 0.07f), StrutCap); // (its top stage's cap, 2 mm down over it)
            }
            // The button panel on the right side's front piston, at a hand's height in the frame.
            var by = MathF.Min(top - 0.15f, pd.Top + MathF.Min(length * 0.55f, 1.1f));
            if (by > pd.Top + 0.1f)
            {
                Box(gfx, m, new Vector3(px - 0.072f, by, -pz), new Vector3(0.06f, 0.16f, 0.12f), Post);
                Box(gfx, m, new Vector3(px - 0.107f, by, -pz), new Vector3(0.012f, 0.06f, 0.06f), Button);
            }
        }
        // The status lamp on the rim's corner: green empty, amber with a shell in it or at work, red broken.
        var rimTop = ConstructShellPod.RimTop(pd);
        var lampAt = pd.Half - 0.03f; // (on the rim's outer half, clear of the trim along its inner edge)
        var status = pp.Broken ? LampBroken : shell || ConstructShellPod.Holds(pp) ? LampBusy : LampEmpty;
        gfx.Draw(Matrix4x4.CreateScale(0.066f, 0.01f, 0.066f) * Matrix4x4.CreateTranslation(lampAt, rimTop + 0.003f, -lampAt) * m, Cap); // its dark bezel, so it reads on the hazard paint
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 0.014f, 0.05f) * Matrix4x4.CreateTranslation(lampAt, rimTop + 0.011f, -lampAt) * m, status, mode: 2); // a glowing lens, 5 cm
        if (pd.Flush && surface >= 0 && surface < pd.Depth - pd.Top - 0.03f)
            Flange(gfx, m, pd, surface);
        // The platform: from the pod's floor up to the lid.
        var plat = bot + 0.08f + lift * (pd.Top - 0.012f - (bot + 0.08f));
        if (lift > 0.01f || shell)
            gfx.Draw(Matrix4x4.CreateScale((px - 0.05f) * 2, 0.04f, inner * 2 - 0.06f) * Matrix4x4.CreateTranslation(0, plat - 0.02f, 0) * m, Plate, // (narrower than the frame's sides: it rises between the pistons)
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

    /// <summary>
    /// A telescoping piston at <paramref name="at"/> (x, z in the pod's frame): its stages nested in the pod, 4 mm apart in height
    /// so no two faces meet on a plane, the stowed tops under the lid; drawn out to <paramref name="head"/> (where the head's
    /// underside is), the thinnest stage first. The stages are as many as the full rise (<paramref name="full"/>) needs, each
    /// keeping 6 cm in the one below. Returns the top of its top stage. Allocates nothing.
    /// </summary>
    private static float Piston(Renderer gfx, in Matrix4x4 m, Vector3 at, float lid, float floor, float head, float full)
    {
        const float Lift = 0.004f, Overlap = 0.06f;
        int n;
        float y0 = 0, len = 0;
        for (n = 2; n < 7; n++)
        {
            y0 = lid - 0.04f - Lift * (n - 1); // (the stowed tops: under a flush pod's flaps, inside a bolted-on pod's head)
            len = MathF.Max(0.05f, y0 - floor);
            if (y0 + Lift * (n - 1) + (n - 1) * (len - Overlap - Lift) >= full)
                break;
        }
        var reach = MathF.Max(0.04f, len - Overlap - Lift); // (the most a stage draws out of the one below)
        var rest = MathF.Max(0, head - (y0 + Lift * (n - 1)));
        Span<float> off = stackalloc float[8];
        for (var k = n - 1; k >= 1; k--)
        {
            off[k] = MathF.Min(rest, reach); // (the thinnest goes first)
            rest -= off[k];
        }
        var step = MathF.Min(0.012f, 0.04f / (n - 1));
        var sum = 0f;
        var top = y0;
        for (var k = 0; k < n; k++)
        {
            sum += k > 0 ? off[k] : 0;
            var lo = y0 - len + Lift * k + sum;
            var hi = y0 + Lift * k + sum;
            var w = 0.08f - step * k;
            Box(gfx, m, new Vector3(at.X, (lo + hi) * 0.5f, at.Z), new Vector3(w, hi - lo, w), (k & 1) == 0 ? Post : Stage);
            top = hi;
        }
        return top;
    }

    /// <summary>
    /// A lighting truss between a side's two pistons at x <paramref name="x"/>, its lower chords at <paramref name="y"/>: three chords
    /// along z (two below, one above) and zig-zag webbing on each of its three faces, the struts of a face alternately 2 mm either
    /// side of it so where they meet no faces share a plane. Allocates nothing.
    /// </summary>
    private static void Truss(Renderer gfx, in Matrix4x4 m, float x, float y, float pz)
    {
        const float Half = 0.022f, Rise = 0.04f, Chord = 0.014f, Web = 0.007f;
        var a = new Vector3(x - Half, y, 0);
        var b = new Vector3(x + Half, y, 0);
        var c = new Vector3(x, y + Rise, 0);
        foreach (var p in (ReadOnlySpan<Vector3>)[a, b, c])
            Box(gfx, m, p, new Vector3(Chord, Chord, pz * 2), TrussMetal); // (their ends in the pistons)
        var bays = Math.Max(2, (int)MathF.Round(pz * 2 / 0.1f));
        for (var f = 0; f < 3; f++)
        {
            var (p0, p1) = f switch { 0 => (a, b), 1 => (b, c), _ => (c, a) };
            var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, Vector3.UnitZ));
            for (var k = 0; k < bays; k++)
            {
                var z0 = -pz + pz * 2 * k / bays;
                var z1 = -pz + pz * 2 * (k + 1) / bays;
                var shift = normal * ((k & 1) == 0 ? 0.002f : -0.002f);
                var from = ((k & 1) == 0 ? p0 : p1) + new Vector3(0, 0, z0) + shift;
                var to = ((k & 1) == 0 ? p1 : p0) + new Vector3(0, 0, z1) + shift;
                Strut(gfx, m, from, to, Web, TrussMetal);
            }
        }
    }

    /// <summary>A thin square strut from <paramref name="from"/> to <paramref name="to"/> in the frame <paramref name="m"/>.</summary>
    private static void Strut(Renderer gfx, in Matrix4x4 m, Vector3 from, Vector3 to, float thick, Vector3 colour)
    {
        var d = to - from;
        var len = d.Length();
        if (len < 1e-4f)
            return;
        var z = d / len;
        var t = MathF.Abs(z.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var x = Vector3.Normalize(Vector3.Cross(t, z));
        var y = Vector3.Cross(z, x);
        var mid = (from + to) * 0.5f;
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, mid.X, mid.Y, mid.Z, 1);
        gfx.Draw(Matrix4x4.CreateScale(thick, thick, len) * basis * m, colour);
    }

    /// <summary>
    /// A shallow flush pod (the user, 2026-11-19: "Shallow flush"): its surface is thinner than its body, so the body stands proud on
    /// the far side as a housing, and a flange with a bolt at each corner rings it 3 mm off that face, where it comes through.
    /// </summary>
    private static void Flange(Renderer gfx, in Matrix4x4 m, ShellPodDef pd, float surface)
    {
        var w = pd.Half;
        var y = -surface - 0.014f; // (from 3 mm to 25 mm past the far face)
        const float H = 0.022f, Out = 0.04f;
        var span = (w + Out) * 2;
        foreach (var s in (ReadOnlySpan<float>)[-1f, 1f])
        {
            Box(gfx, m, new Vector3(s * (w + Out * 0.5f - 0.0015f), y, 0), new Vector3(Out + 0.003f, H, span), Flange22); // (its inner edge in the wall)
            Box(gfx, m, new Vector3(0, y, s * (w + Out * 0.5f - 0.0015f)), new Vector3((w - 0.005f) * 2, H, Out + 0.003f), Flange22);
        }
        foreach (var sx in (ReadOnlySpan<float>)[-1f, 1f])
            foreach (var sz in (ReadOnlySpan<float>)[-1f, 1f])
                Box(gfx, m, new Vector3(sx * (w + 0.02f), y - H * 0.5f - 0.003f, sz * (w + 0.02f)), new Vector3(0.024f, 0.012f, 0.024f), Bolt);
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
