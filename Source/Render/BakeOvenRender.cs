using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a Bake Oven draws over its blocks (ConstructBakeOven.cs; the Bake Oven Lab's roller hearth tunnel, Sean 2026-11-24), in the
/// tunnel's frame (<see cref="ConstructBakeOven.Bed.ToCore"/>: x across the bed, z along it from the in end, y up from the foot):
/// ceramic hearth rollers on two stands with their tops at the roll line; fixed side walls lined with firebrick, each with a ribbon of
/// heat glass at eye height (a mullion each metre, a heavy sill and head, a rain hood) and radiant tubes below and above it; a roof
/// lined with firebrick, a rib across it each 2 m and a burner box with its flue every 4 m; ribs outside each 2 m cell line; at each end
/// a portal (posts over the walls' ends, a head with a hazard edge, a sill with a hazard edge) and a pair of swing doors hinged on its
/// posts that swing outward, away from the tunnel. The Core's duct runs from its drums' stub to the tunnel, ending at a yellow flange,
/// and its blower's fan spins faster as it heats.
/// <para>
/// While it bakes the tubes, the glass and the lining warm and the flues breathe heat haze; it cools; the doors swing open. The lining
/// keeps a dim warm fill glow at all times (a glow, not a light). All from the phase byte and the time since this peer saw it change, so
/// a client draws what the host does. Drawn within <see cref="DrawNear"/>, the small pieces within <see cref="DrawFine"/>. Allocates nothing.
/// </para>
/// </summary>
public static class BakeOvenRender
{
    public const float DrawNear = 150f, DrawFine = 60f;
    private static readonly Vector3 Graphite = new(0.227f, 0.247f, 0.275f), Trim = new(0.173f, 0.188f, 0.212f), Base = new(0.133f, 0.149f, 0.169f),
        Steel = new(0.6f, 0.63f, 0.66f), Mast = new(0.29f, 0.314f, 0.345f), Yellow = new(0.95f, 0.69f, 0.12f), Black = new(0.08f, 0.09f, 0.1f),
        Brick = new(0.54f, 0.294f, 0.204f), Fill = new(0.78f, 0.47f, 0.31f), Hot = new(1f, 0.42f, 0.1f), Roller = new(0.79f, 0.72f, 0.64f),
        Tube = new(0.29f, 0.23f, 0.2f), Glass = new(1f, 0.82f, 0.63f), Duct = new(0.66f, 0.68f, 0.7f);
    private const float CoolFade = 3f, LeafThick = 0.18f;
    private static double _last;
    private static CPartBox? _cyl; // a "+y" cylinder box of the Core's own (its drum): the shape the round pieces are drawn with
    private static bool _cylLooked;

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        var now = ConstructRender.Now;
        var dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        foreach (var c in world.Bodies)
        {
            if (c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Core >= c.PartCount || !c.Parts[asm.Core].D.IsBakeOven || asm.W == 0 || world.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var bed = new ConstructBakeOven.Bed(c, asm);
                DrawOven(gfx, c, asm, bed, st, now, dt, eye);
            }
        }
    }

    private static void DrawOven(Renderer gfx, Construct c, in CMachineAsm asm, in ConstructBakeOven.Bed bed, CStore st, double now, float dt, Vector3 eye)
    {
        var d = c.Parts[asm.Core].D;
        if (!_cylLooked)
        {
            _cylLooked = true;
            foreach (var b in d.Boxes)
                if (b.Shape != 0)
                {
                    _cyl = b;
                    break;
                }
        }
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var core = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var tf = bed.ToCore * core;
        // The phase as this peer saw it change (a client's clock: the byte comes with the store's next send).
        var phase = (OvenPhase)st.Blocked;
        if (st.OvenSeen != st.Blocked || st.OvenSince == 0)
            (st.OvenSeen, st.OvenSince) = (st.Blocked, now);
        var age = (float)(now - st.OvenSince);
        var shut = ConstructBakeOven.DoorsShut(phase, age / MathF.Max(0.5f, d.BakeDoorSeconds));
        var heat = phase switch { OvenPhase.Heat => Math.Min(1f, age / 1.5f), OvenPhase.Cool => Math.Max(0f, 1f - age / CoolFade), _ => 0f };
        var mid = Vector3.Transform(new Vector3(bed.Across * 0.5f, bed.Roll, bed.Along * 0.5f), tf) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
        var fine = Vector3.DistanceSquared(eye, mid) < (DrawFine + bed.Along * 0.5f) * (DrawFine + bed.Along * 0.5f);
        float bw = bed.Across, bl = bed.Along, yR = bed.Roof, top = bed.Top, roll = bed.Roll, wt = ConstructBakeOven.Wall, dz = ConstructBakeOven.DoorZone;
        var cx = bw * 0.5f;
        var lining = Vector3.Lerp(Brick * 0.5f + Fill * 0.18f, Hot * 0.9f, heat * 0.6f); // (the fill glow: always a little, more while it bakes)
        var tube = Vector3.Lerp(Tube, Hot, heat);
        // ---- The hearth: two stands along the bed and the ceramic rollers across them, tops at the roll line ----
        foreach (var x in new[] { 0.33f, bw - 0.45f })
            Box(gfx, tf, new Vector3(x, top + 0.004f, dz), new Vector3(x + 0.12f, roll - 0.03f, bl - dz), Base);
        if (fine)
            for (var z = dz + 0.25f; z < bl - dz - 0.1f; z += 0.5f)
                Cyl(gfx, tf, 0, new Vector3(cx, roll - 0.09f, z), 0.18f, bw - 0.9f, Roller);
        // ---- The side walls, their lining, ribbon windows and radiant tubes ----
        var windows = bed.Clear >= 1.4f;
        float wy0 = 1.4f, wy1 = MathF.Min(2.3f, yR - 0.35f), za = dz + 0.3f, zb = bl - dz - 0.3f;
        if (wy1 < wy0 + 0.3f || zb < za + 0.5f)
            windows = false;
        for (var s = 0; s < 2; s++)
        {
            float x0 = s == 1 ? bw - wt : 0, x1 = s == 1 ? bw : wt;
            float lx0 = s == 1 ? x0 - 0.03f : x1, lx1 = s == 1 ? x0 : x1 + 0.03f; // (the lining on its inner face)
            float o = s == 1 ? bw : 0, outS = s == 1 ? 1 : -1; // (its outer face, and which way is out)
            if (!windows)
            {
                Box(gfx, tf, new Vector3(x0, top, dz), new Vector3(x1, yR, bl - dz), Graphite);
                Box(gfx, tf, new Vector3(lx0, top, dz), new Vector3(lx1, yR - 0.03f, bl - dz), lining, 2);
            }
            else
            {
                Box(gfx, tf, new Vector3(x0, top, dz), new Vector3(x1, wy0, bl - dz), Graphite);
                Box(gfx, tf, new Vector3(x0, wy1, dz), new Vector3(x1, yR, bl - dz), Graphite);
                Box(gfx, tf, new Vector3(x0, wy0, dz), new Vector3(x1, wy1, za), Graphite);
                Box(gfx, tf, new Vector3(x0, wy0, zb), new Vector3(x1, wy1, bl - dz), Graphite);
                Box(gfx, tf, new Vector3(lx0, top, dz), new Vector3(lx1, wy0, bl - dz), lining, 2);
                Box(gfx, tf, new Vector3(lx0, wy1, dz), new Vector3(lx1, yR - 0.03f, bl - dz), lining, 2);
                Box(gfx, tf, new Vector3(lx0, wy0, dz), new Vector3(lx1, wy1, za), lining, 2);
                Box(gfx, tf, new Vector3(lx0, wy0, zb), new Vector3(lx1, wy1, bl - dz), lining, 2);
                // The glass (a pane in the blended pass), warming while it bakes; behind it the hearth's glow shows.
                var xm = (x0 + x1) * 0.5f;
                ConstructRender.QueueGlass(Scale(new Vector3(xm - 0.01f, wy0, za), new Vector3(xm + 0.01f, wy1, zb)) * tf, Vector3.Lerp(Glass, Hot, heat * 0.7f), 0.3f + 0.25f * heat, 0);
                // A heavy sill and head inside the opening, a mullion each metre, a rain hood and an outer sill.
                Box(gfx, tf, new Vector3(xm - 0.05f, wy0 - 0.04f, za), new Vector3(xm + 0.05f, wy0 + 0.03f, zb), Base);
                Box(gfx, tf, new Vector3(xm - 0.05f, wy1 - 0.03f, za), new Vector3(xm + 0.05f, wy1 + 0.04f, zb), Base);
                var mullions = Math.Max(1, (int)MathF.Round(zb - za));
                if (fine)
                    for (var k = 1; k < mullions; k++)
                    {
                        var z = za + (zb - za) * k / mullions;
                        Box(gfx, tf, new Vector3(xm - 0.04f, wy0 + 0.03f, z - 0.025f), new Vector3(xm + 0.04f, wy1 - 0.03f, z + 0.025f), Base);
                    }
                Box(gfx, tf, Lo(o, outS, 0.25f, wy1 + 0.004f, za - 0.1f), Hi(o, outS, 0.25f, wy1 + 0.08f, zb + 0.1f), Trim);
                Box(gfx, tf, Lo(o, outS, 0.16f, wy0 - 0.08f, za), Hi(o, outS, 0.16f, wy0 - 0.004f, zb), Steel);
            }
            if (fine)
            {
                // The radiant tubes along the inner face, under the rollers' ends and under the roof.
                var tx = s == 1 ? x0 - 0.13f : x1 + 0.13f;
                foreach (var y in windows ? new[] { 0.72f, yR - 0.3f } : new[] { roll + bed.Clear * 0.38f, roll + bed.Clear * 0.75f })
                    Cyl(gfx, tf, 2, new Vector3(tx, y, bl * 0.5f), 0.14f, bl - 2 * dz - 0.2f, tube, 2);
                // Ribs outside at each 2 m cell line.
                for (var z = 2f; z < bl - 0.5f; z += 2f)
                    Box(gfx, tf, Lo(o, outS, 0.08f, top + 0.004f, z - 0.06f), Hi(o, outS, 0.08f, yR - 0.004f, z + 0.06f), Trim);
            }
        }
        // ---- The roof and its lining; ribs across it, burner boxes and their flues ----
        Box(gfx, tf, new Vector3(0, yR, 0), new Vector3(bw, yR + 0.5f, bl), Graphite);
        Box(gfx, tf, new Vector3(wt + 0.03f, yR - 0.03f, dz), new Vector3(bw - wt - 0.03f, yR, bl - dz), lining, 2);
        if (fine)
        {
            for (var z = 2f; z < bl - 0.5f; z += 2f)
                Box(gfx, tf, new Vector3(-0.04f, yR + 0.5f, z - 0.05f), new Vector3(bw + 0.04f, yR + 0.6f, z + 0.05f), Trim);
            for (var z = 2f; z < bl - 1f; z += 4f)
            {
                Box(gfx, tf, new Vector3(cx - 0.5f, yR + 0.5f, z + 0.6f), new Vector3(cx + 0.5f, yR + 0.9f, z + 1.4f), Base);
                Box(gfx, tf, new Vector3(cx - 0.42f, yR + 0.9f, z + 0.68f), new Vector3(cx + 0.42f, yR + 0.94f, z + 1.32f), Steel); // (its lid)
                Cyl(gfx, tf, 1, new Vector3(cx + 0.2f, yR + 1.3f, z + 1f), 0.25f, 0.72f, Base);
                Cyl(gfx, tf, 1, new Vector3(cx + 0.2f, yR + 1.68f, z + 1f), 0.33f, 0.04f, Steel); // (its band)
                if (heat > 0.3f && Fx.On)
                {
                    var at = Vector3.Transform(new Vector3(cx + 0.2f, yR + 1.75f, z + 1f), tf) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                    var up = Vector3.TransformNormal(Vector3.UnitY, tf);
                    for (var n = Fx.Share(2.5f * heat * dt); n > 0; n--)
                        Fx.Emit(FxKind.Steam, at, c.Vel + up * 1.2f, 0.35f, 2.2f);
                }
            }
        }
        // ---- The portals and their swing doors, at both ends ----
        float w = (bw - 2 * wt) * 0.5f - 0.01f;
        var leafTop = yR - 0.03f;
        var open = (1f - shut) * ConstructBakeOven.SwingDeg * MathF.PI / 180f;
        for (var e = 0; e < 2; e++)
        {
            float z0 = e == 0 ? 0 : bl - dz, z1 = e == 0 ? dz : bl, outZ = e == 0 ? -1 : 1, face = e == 0 ? 0 : bl;
            // The posts over the walls' ends, a little proud of them; the head on the roof's end face, a hazard edge under it.
            Box(gfx, tf, new Vector3(-0.03f, top, z0), new Vector3(wt + 0.02f, yR, z1), Mast);
            Box(gfx, tf, new Vector3(bw - wt - 0.02f, top, z0), new Vector3(bw + 0.03f, yR, z1), Mast);
            Box(gfx, tf, new Vector3(-0.04f, yR - 0.12f, Math.Min(face, face + outZ * 0.05f)), new Vector3(bw + 0.04f, yR + 0.52f, Math.Max(face, face + outZ * 0.05f)), Trim);
            Stripes(gfx, tf, -0.04f, bw + 0.04f, yR - 0.12f, 0.1f, face + (e == 0 ? -0.056f : 0.05f), 0.006f);
            // The sill the leaves close on, a hazard edge on its outer face.
            Box(gfx, tf, new Vector3(wt + 0.03f, top + 0.004f, z0 + 0.05f), new Vector3(bw - wt - 0.03f, roll - 0.02f, z1 - 0.05f), Base);
            if (fine)
                Stripes(gfx, tf, wt + 0.03f, bw - wt - 0.03f, roll - 0.14f, 0.12f, e == 0 ? z0 + 0.044f : z1 - 0.05f, 0.006f);
            // The leaves: hinged on the posts' inner faces at the door line, each swinging out by its own side.
            var zl = e == 0 ? 0.25f : bl - 0.25f;
            for (var sd = -1; sd <= 1; sd += 2)
            {
                var hinge = new Vector3(sd < 0 ? bw - wt - 0.02f : wt + 0.02f, 0, zl); // (sd +1: the leaf on the low side, reaching +x)
                var turn = sd * (e == 0 ? 1 : -1) * open;
                var lf = Matrix4x4.CreateRotationY(turn) * Matrix4x4.CreateTranslation(hinge) * tf;
                float a0 = sd > 0 ? 0 : -w, a1 = sd > 0 ? w : 0, half = LeafThick * 0.5f, inS = -outZ; // (inS: towards the tunnel)
                Box(gfx, lf, new Vector3(a0, roll + 0.01f, -half), new Vector3(a1, leafTop, half), Graphite);
                // The firebrick face inside, the ribs outside, a hazard edge along its foot outside.
                Box(gfx, lf, new Vector3(a0 + 0.08f, roll + 0.09f, inS > 0 ? half : -half - 0.012f), new Vector3(a1 - 0.08f, leafTop - 0.08f, inS > 0 ? half + 0.012f : -half), lining, 2);
                if (fine)
                {
                    for (var y = roll + 0.5f; y < leafTop - 0.2f; y += 0.7f)
                        Box(gfx, lf, new Vector3(a0 + 0.05f, y - 0.04f, outZ > 0 ? half : -half - 0.03f), new Vector3(a1 - 0.05f, y + 0.04f, outZ > 0 ? half + 0.03f : -half), Trim);
                    Box(gfx, lf, new Vector3(a0 + 0.05f, roll + 0.1f, outZ > 0 ? half : -half - 0.03f), new Vector3(a0 + 0.13f, leafTop - 0.1f, outZ > 0 ? half + 0.03f : -half), Trim); // (a stile at each edge)
                    Box(gfx, lf, new Vector3(a1 - 0.13f, roll + 0.1f, outZ > 0 ? half : -half - 0.03f), new Vector3(a1 - 0.05f, leafTop - 0.1f, outZ > 0 ? half + 0.03f : -half), Trim);
                    Stripes(gfx, lf, a0 + 0.004f, a1 - 0.004f, roll + 0.012f, 0.12f, outZ > 0 ? half + 0.002f : -half - 0.008f, 0.006f);
                    foreach (var y in new[] { roll + 0.4f, leafTop - 0.4f })
                        Cyl(gfx, lf, 1, new Vector3(0, y, 0), 0.12f, 0.3f, Steel); // (its hinge knuckles)
                }
            }
        }
        // ---- The Core: its duct to the tunnel and its blower's fan ----
        DrawCore(gfx, core, bed, heat, now, fine);
    }

    /// <summary>
    /// The Core's duct: from the elbow on its drums' stub straight across to the tunnel, ending at a yellow flange on the wall (or down onto
    /// the roof if the tunnel is lower than the elbow), and the blower's fan, turning faster with the heat. In the Core part's frame.
    /// </summary>
    private static void DrawCore(Renderer gfx, Matrix4x4 core, in ConstructBakeOven.Bed bed, float heat, double now, bool fine)
    {
        if (!Matrix4x4.Invert(bed.ToCore, out var toTunnel))
            return;
        var elbow = new Vector3(0.4f, 4.41f, 0.25f);
        var p = Vector3.Transform(elbow, toTunnel);
        var wallX = p.X < bed.Across * 0.5f ? 0f : bed.Across; // (the wall facing the Core)
        var z = Math.Clamp(p.Z, ConstructBakeOven.DoorZone + 0.4f, bed.Along - ConstructBakeOven.DoorZone - 0.4f);
        var tf = bed.ToCore * core;
        var y = p.Y;
        var low = bed.Roof + 0.5f < y - 0.3f; // (the roof's top under the duct: it turns down onto the roof)
        var end = new Vector3(low ? wallX + (wallX > 0 ? -0.6f : 0.6f) : wallX, y, z);
        var from = new Vector3(p.X, y, p.Z);
        Pipe(gfx, tf, from, end, 0.42f, Duct);
        Pipe(gfx, tf, from + Vector3.Normalize(end - from) * 0.25f, from + Vector3.Normalize(end - from) * 0.3f, 0.5f, Base); // (a band near the elbow)
        if (low)
        {
            var foot = end with { Y = bed.Roof + 0.5f };
            Pipe(gfx, tf, end + new Vector3(0, 0.21f, 0), foot, 0.42f, Duct);
            Pipe(gfx, tf, foot, foot + new Vector3(0, 0.06f, 0), 0.6f, Yellow);
        }
        else
        {
            var dir = Vector3.Normalize(end - from);
            Pipe(gfx, tf, end - dir * 0.06f, end, 0.6f, Yellow); // (the flange it meets the wall at)
        }
        if (!fine)
            return;
        // The fan in the blower's mouth (its axis along the Core's x), turning faster with the heat.
        var turn = (float)(now * (2 + heat * 14) % (2 * Math.PI));
        var hub = new Vector3(0.1f, 0.75f, -0.6f);
        for (var k = 0; k < 2; k++)
        {
            var m = Matrix4x4.CreateScale(0.02f, 0.62f, 0.06f) * Matrix4x4.CreateRotationX(turn + k * MathF.PI / 2) * Matrix4x4.CreateTranslation(hub) * core;
            gfx.Draw(m, Black);
        }
    }

    /// <summary>A round pipe from <paramref name="a"/> to <paramref name="b"/> (both in <paramref name="frame"/>), <paramref name="dia"/> across.</summary>
    private static void Pipe(Renderer gfx, Matrix4x4 frame, Vector3 a, Vector3 b, float dia, Vector3 colour)
    {
        var dir = b - a;
        var len = dir.Length();
        if (len < 1e-3f)
            return;
        var y = dir / len;
        var x = Vector3.Normalize(Vector3.Cross(y, MathF.Abs(y.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var zz = Vector3.Cross(x, y);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, zz.X, zz.Y, zz.Z, 0, 0, 0, 0, 1);
        var place = basis * Matrix4x4.CreateTranslation((a + b) * 0.5f) * frame;
        Round(gfx, place, new Vector3(dia, len, dia), colour, 0);
    }

    /// <summary>A cylinder along <paramref name="axis"/> (0 x, 1 y, 2 z) at <paramref name="centre"/>, <paramref name="dia"/> across and <paramref name="len"/> long.</summary>
    private static void Cyl(Renderer gfx, Matrix4x4 frame, int axis, Vector3 centre, float dia, float len, Vector3 colour, int mode = 0)
    {
        var turn = axis switch { 0 => Matrix4x4.CreateRotationZ(MathF.PI / 2), 2 => Matrix4x4.CreateRotationX(MathF.PI / 2), _ => Matrix4x4.Identity };
        Round(gfx, turn * Matrix4x4.CreateTranslation(centre) * frame, new Vector3(dia, len, dia), colour, mode);
    }

    /// <summary>A "+y" cylinder <paramref name="size"/> big about the origin of <paramref name="place"/> (a box if the Core has no round box).</summary>
    private static void Round(Renderer gfx, Matrix4x4 place, Vector3 size, Vector3 colour, int mode)
    {
        if (_cyl is { } cyl)
            gfx.Draw((cyl with { Half = size * 0.5f, Center = Vector3.Zero }).Model(0, Vector3.Zero) * place, colour, mode: mode, shape: cyl.Shape);
        else
            gfx.Draw(Matrix4x4.CreateScale(size) * place, colour, mode: mode);
    }

    /// <summary>The low and high corners of a box on a wall's outer face at <paramref name="o"/>, <paramref name="deep"/> out (way <paramref name="outS"/>).</summary>
    private static Vector3 Lo(float o, float outS, float deep, float y, float z) => new(MathF.Min(o + outS * 0.003f, o + outS * deep), y, z);
    private static Vector3 Hi(float o, float outS, float deep, float y, float z) => new(MathF.Max(o + outS * 0.003f, o + outS * deep), y, z);

    /// <summary>Hazard stripes along x from <paramref name="a0"/> to <paramref name="a1"/>: yellow and black blocks 0.25 m long, <paramref name="h"/> high, on a face at z <paramref name="at"/>.</summary>
    private static void Stripes(Renderer gfx, Matrix4x4 frame, float a0, float a1, float y, float h, float at, float deep)
    {
        const float step = 0.25f;
        var k = 0;
        for (var a = a0; a < a1 - 0.01f; a += step, k++)
        {
            var b = MathF.Min(a + step, a1);
            gfx.Draw(Scale(new Vector3(a, y, at), new Vector3(b, y + h, at + deep)) * frame, (k & 1) == 0 ? Yellow : Black);
        }
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour, int mode = 0) => gfx.Draw(Scale(lo, hi) * frame, colour, mode: mode);
}
