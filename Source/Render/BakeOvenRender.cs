using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a Bake Oven draws across its blocks (ConstructBakeOven.cs; design page drawings 19 and 24), from its Core's phase byte, which
/// every peer has: the hood over the bed's clear cells, one section a bed row, each a pale steel roof hung under the mast tops with a
/// dark rim, ribs and stiffeners, a vent box with a glowing slot on every other cell, and skirts that telescope down from its outer
/// edges to the bed with hazard stripes round their foot; a screw column on the inner face of each corner mast with the yoke that
/// drives the skirts riding it. The oven has no crane of its own: a hall crane, a Loader Arm or a roller run (ConstructRollOut.cs)
/// loads and empties the bed.
/// <para>
/// The skirts drop in a quick ripple, row after row; while it bakes its seam and vents glow and the vents breathe heat haze;
/// the bed's heated edge fades as it cools; then the hood lifts. All from the phase and the time since this peer saw it change, so a
/// client draws what the host does. Drawn within <see cref="DrawNear"/>. Allocates nothing.
/// </para>
/// </summary>
public static class BakeOvenRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Hood = new(0.85f, 0.83f, 0.78f), HoodDark = new(0.62f, 0.6f, 0.56f), Dark = new(0.133f, 0.149f, 0.169f),
        Steel = new(0.6f, 0.63f, 0.66f), Yellow = new(0.94f, 0.76f, 0.1f), Hot = new(1f, 0.5f, 0.12f), Black = new(0.08f, 0.08f, 0.09f);
    private const float CoolFade = 3f;
    private static double _last;

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
                if (bed.Columns == 0)
                    continue; // (no masts: no hood to hang)
                DrawOven(gfx, c, asm, bed, st, now, dt);
            }
        }
    }

    private static void DrawOven(Renderer gfx, Construct c, in CMachineAsm asm, in ConstructBakeOven.Bed bed, CStore st, double now, float dt)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        // The phase as this peer saw it change (a client's clock: the byte comes with the store's next send).
        var phase = (OvenPhase)st.Blocked;
        if (st.OvenSeen != st.Blocked || st.OvenSince == 0)
            (st.OvenSeen, st.OvenSince) = (st.Blocked, now);
        var age = (float)(now - st.OvenSince);
        var move = MathF.Max(1f, bed.Clear / d.BakeHoodSpeed);
        var t = Math.Clamp(age / move, 0f, 1f);
        var heat = phase switch { OvenPhase.Heat => Math.Min(1f, age / 1.5f), OvenPhase.Cool => Math.Max(0f, 1f - age / CoolFade), _ => 0f };
        var cell = bed.Cell;
        var half = cell * 0.5f;
        var o = d.BlockOrigin ?? Vector3.Zero;
        float X(int x) => o.X + x * cell; // a cell's middle across
        float Z(int z) => o.Z + z * cell; // ...and along
        var rows = bed.L;
        // ---- The hood: a section over each row's runs of clear cells ----
        for (var z = bed.Z0; z < bed.Z0 + bed.L; z++)
        {
            var down = ConstructBakeOven.HoodDown(phase, t, z - bed.Z0, rows);
            var y0 = bed.Raised; // the roof's underside: it stays hung from the masts
            var y1 = y0 + ConstructBakeOven.HoodThick;
            var yf = bed.Raised - (bed.Clear - 0.02f) * down; // the skirts' foot: raised, or 2 cm over the bed
            for (var x = bed.X0; x < bed.X0 + bed.W; x++)
            {
                if (!bed.Free(c, asm, x, z) || x > bed.X0 && bed.Free(c, asm, x - 1, z))
                    continue; // (only from the first cell of a run)
                var x1 = x;
                while (x1 + 1 < bed.X0 + bed.W && bed.Free(c, asm, x1 + 1, z))
                    x1++;
                float ax = X(x) - half + 0.012f, bx = X(x1) + half - 0.012f, az = Z(z) - half + 0.004f, bz = Z(z) + half - 0.004f; // (rows 8 mm apart, so a ripple shows each section's edge)
                var open0 = !bed.Free(c, asm, x, z - 1);
                var open1 = !bed.Free(c, asm, x, z + 1);
                // The roof: a pale box with a darker band at its foot; ribs across, stiffeners along each cell's edge.
                Box(gfx, frame, new Vector3(ax, y0 + 0.12f, az), new Vector3(bx, y1, bz), Hood);
                Box(gfx, frame, new Vector3(ax + 0.01f, y0, az + 0.01f), new Vector3(bx - 0.01f, y0 + 0.12f, bz - 0.01f), Dark);
                Box(gfx, frame, new Vector3(ax + 0.05f, y1, Z(z) - 0.05f), new Vector3(bx - 0.05f, y1 + 0.1f, Z(z) + 0.05f), HoodDark);
                for (var k = x; k <= x1 + 1; k++)
                {
                    var kx = X(k) - half;
                    var lo = MathF.Max(ax + 0.03f, kx - 0.05f);
                    var hi = MathF.Min(bx - 0.03f, kx + 0.05f);
                    if (hi > lo)
                        Box(gfx, frame, new Vector3(lo, y1, az + 0.05f), new Vector3(hi, y1 + 0.14f, bz - 0.05f), HoodDark);
                }
                // The skirts: pale walls that telescope down out of the roof on the section's outer edges, so a closed hood
                // is a box over the group; a rib up each cell's edge.
                if (yf < y0 + 0.1f)
                {
                    const float sk = 0.05f, ins = 0.015f;
                    float sy1 = y0 + 0.11f;
                    Box(gfx, frame, new Vector3(ax + ins, yf, az + ins), new Vector3(ax + ins + sk, sy1, bz - ins), Hood);
                    Box(gfx, frame, new Vector3(bx - ins - sk, yf, az + ins), new Vector3(bx - ins, sy1, bz - ins), Hood);
                    if (open0)
                        Box(gfx, frame, new Vector3(ax + ins + sk + 0.004f, yf, az + ins), new Vector3(bx - ins - sk - 0.004f, sy1, az + ins + sk), Hood);
                    if (open1)
                        Box(gfx, frame, new Vector3(ax + ins + sk + 0.004f, yf, bz - ins - sk), new Vector3(bx - ins - sk - 0.004f, sy1, bz - ins), Hood);
                    for (var k = x; k <= x1 + 1; k++)
                    {
                        var kx = Math.Clamp(X(k) - half, ax + ins + 0.06f, bx - ins - 0.06f);
                        if (open0)
                            Box(gfx, frame, new Vector3(kx - 0.07f, yf + 0.15f, az + ins - 0.06f), new Vector3(kx + 0.07f, sy1 - 0.02f, az + ins - 0.003f), HoodDark);
                        if (open1)
                            Box(gfx, frame, new Vector3(kx - 0.07f, yf + 0.15f, bz - ins + 0.003f), new Vector3(kx + 0.07f, sy1 - 0.02f, bz - ins + 0.06f), HoodDark);
                    }
                    // Two belts round each skirt, a third and two thirds up (the panels' joins), so a closed hood isn't a plain wall.
                    for (var belt = 1; belt <= 2; belt++)
                    {
                        var by = yf + (sy1 - yf) * belt / 3f;
                        if (sy1 - yf < 0.6f)
                            break;
                        if (open0)
                            Box(gfx, frame, new Vector3(ax + ins, by - 0.06f, az + ins - 0.04f), new Vector3(bx - ins, by + 0.06f, az + ins - 0.004f), Dark);
                        if (open1)
                            Box(gfx, frame, new Vector3(ax + ins, by - 0.06f, bz - ins + 0.004f), new Vector3(bx - ins, by + 0.06f, bz - ins + 0.04f), Dark);
                        Box(gfx, frame, new Vector3(ax + ins - 0.04f, by - 0.06f, az + ins), new Vector3(ax + ins - 0.004f, by + 0.06f, bz - ins), Dark);
                        Box(gfx, frame, new Vector3(bx - ins + 0.004f, by - 0.06f, az + ins), new Vector3(bx - ins + 0.04f, by + 0.06f, bz - ins), Dark);
                    }
                    // Angle iron up the section's corners (dark, bolted), and vertical ribs on its x ends at each cell edge along z.
                    for (var e = 0; e < 2; e++)
                    {
                        var fx = e == 0 ? ax + ins : bx - ins; // the end skirt's outer face
                        var outS = e == 0 ? -1f : 1f;
                        var ex0 = MathF.Min(fx, fx + outS * 0.07f) + (e == 0 ? 0 : 0.003f);
                        var ex1 = MathF.Max(fx, fx + outS * 0.07f) - (e == 0 ? 0.003f : 0);
                        if (open0)
                            Box(gfx, frame, new Vector3(ex0, yf + 0.15f, az + ins - 0.002f), new Vector3(ex1, sy1 - 0.02f, az + ins + 0.16f), Dark);
                        if (open1)
                            Box(gfx, frame, new Vector3(ex0, yf + 0.15f, bz - ins - 0.16f), new Vector3(ex1, sy1 - 0.02f, bz - ins + 0.002f), Dark);
                        var midZ = Z(z);
                        Box(gfx, frame, new Vector3(ex0 + 0.005f * outS, yf + 0.15f, midZ - 0.06f), new Vector3(ex1 + 0.005f * outS, sy1 - 0.02f, midZ + 0.06f), HoodDark);
                    }
                    // Hazard stripes round the skirts' foot, and the seam glow under them while it bakes.
                    Stripes(gfx, frame, ax + ins, bx - ins, yf + 0.01f, az + ins - 0.007f, true, open0);
                    Stripes(gfx, frame, ax + ins, bx - ins, yf + 0.01f, bz - ins + 0.001f, true, open1);
                    Stripes(gfx, frame, az + ins, bz - ins, yf + 0.01f, ax + ins - 0.007f, false, true);
                    Stripes(gfx, frame, az + ins, bz - ins, yf + 0.01f, bx - ins + 0.001f, false, true);
                }
                // A vent on every other cell: a dark box with a slot that glows while it bakes, and heat haze out of it.
                for (var k = x; k <= x1; k++)
                {
                    if ((k + z & 1) != 0)
                        continue;
                    var vx = X(k) + 0.45f;
                    var vz = Z(z) + 0.45f;
                    Box(gfx, frame, new Vector3(vx - 0.25f, y1, vz - 0.25f), new Vector3(vx + 0.25f, y1 + 0.22f, vz + 0.25f), Dark);
                    var slot = Vector3.Lerp(Black, Hot, heat * (0.85f + 0.15f * MathF.Sin((float)now * 5f + k + z)));
                    gfx.Draw(Scale(new Vector3(vx - 0.18f, y1 + 0.221f, vz - 0.06f), new Vector3(vx + 0.18f, y1 + 0.232f, vz + 0.06f)) * frame, slot, mode: heat > 0 ? 2 : 0);
                    if (heat > 0.3f && Fx.On)
                    {
                        var at = Vector3.Transform(new Vector3(vx, y1 + 0.26f, vz), frame) + (gfx.Relative ? gfx.Origin : Vector3.Zero);
                        var up = Vector3.TransformNormal(Vector3.UnitY, frame);
                        for (var n = Fx.Share(2.5f * heat * dt); n > 0; n--)
                            Fx.Emit(FxKind.Steam, at, c.Vel + up * 1.2f, 0.35f, 2.2f);
                    }
                }
                if (heat > 0)
                {
                    var glow = Hot * heat * (0.9f + 0.1f * MathF.Sin((float)now * 3f + z));
                    gfx.Draw(Scale(new Vector3(ax + 0.009f, yf - 0.012f, az + 0.009f), new Vector3(bx - 0.009f, yf - 0.004f, bz - 0.009f)) * frame, glow, mode: 2);
                }
            }
        }
        // ---- The bed's heated edge: it glows while it bakes and fades as it cools ----
        if (heat > 0)
        {
            float bx0 = X(bed.X0) - half, bx1 = X(bed.X0 + bed.W - 1) + half, bz0 = Z(bed.Z0) - half, bz1 = Z(bed.Z0 + bed.L - 1) + half;
            float ly = bed.Top - 0.16f, hy = bed.Top - 0.1f;
            var glow = Hot * heat;
            gfx.Draw(Scale(new Vector3(bx0, ly, bz0 - 0.03f), new Vector3(bx1, hy, bz0 - 0.017f)) * frame, glow, mode: 2);
            gfx.Draw(Scale(new Vector3(bx0, ly, bz1 + 0.017f), new Vector3(bx1, hy, bz1 + 0.03f)) * frame, glow, mode: 2);
            gfx.Draw(Scale(new Vector3(bx0 - 0.03f, ly, bz0), new Vector3(bx0 - 0.017f, hy, bz1)) * frame, glow, mode: 2);
            gfx.Draw(Scale(new Vector3(bx1 + 0.017f, ly, bz0), new Vector3(bx1 + 0.03f, hy, bz1)) * frame, glow, mode: 2);
        }
        // ---- The screw columns and yokes at the four corner masts ----
        var hoodNow = bed.Raised - (bed.Clear - 0.02f) * ConstructBakeOven.HoodDown(phase, t, rows / 2, rows);
        for (var corner = 0; corner < 4; corner++)
        {
            var cx = (corner & 1) == 0 ? bed.X0 : bed.X0 + bed.W - 1;
            var cz = (corner & 2) == 0 ? bed.Z0 : bed.Z0 + bed.L - 1;
            var sx = cx == bed.X0 ? 1f : -1f; // (towards the bed's middle)
            var sz = cz == bed.Z0 ? 1f : -1f;
            if (bed.W == 1)
                sx = 1f;
            var inner = new Vector2(X(cx) + sx * (half + 0.08f), Z(cz) + sz * (half + 0.08f)); // (the screw: just off the mast's inner corner)
            var r = 0.07f;
            Box(gfx, frame, new Vector3(inner.X - r, bed.Top + 0.05f, inner.Y - r), new Vector3(inner.X + r, bed.MastTop - 0.05f, inner.Y + r), Steel);
            for (var k = 0; k < bed.Columns; k++) // (a bearing block bolted to the mast each 2 m)
            {
                var by = bed.Top + 1f + k * cell;
                Box(gfx, frame, new Vector3(inner.X - r - 0.06f, by - 0.08f, inner.Y - r - 0.06f), new Vector3(inner.X + r + 0.06f, by + 0.08f, inner.Y + r + 0.06f), Dark);
            }
            // The yoke riding the screw: a nut housing and an arm into the skirts' corner, which it drives down.
            var yy = MathF.Min(hoodNow + 0.3f, bed.Raised - 0.2f);
            Box(gfx, frame, new Vector3(inner.X - 0.16f, yy - 0.18f, inner.Y - 0.16f), new Vector3(inner.X + 0.16f, yy + 0.18f, inner.Y + 0.16f), Yellow);
            var reach = new Vector3(inner.X + sx * 0.5f, yy, inner.Y + sz * 0.5f);
            Box(gfx, frame, Vector3.Min(new Vector3(inner.X, yy - 0.08f, inner.Y), reach - new Vector3(0, 0.08f, 0)) - new Vector3(0.06f, 0, 0.06f),
                Vector3.Max(new Vector3(inner.X, yy + 0.08f, inner.Y), reach + new Vector3(0, 0.08f, 0)) + new Vector3(0.06f, 0, 0.06f), Dark);
        }
    }

    /// <summary>Hazard stripes along a hood's foot: yellow and black blocks 0.25 m long, on a face at <paramref name="at"/> (along x when <paramref name="alongX"/>, else along z).</summary>
    private static void Stripes(Renderer gfx, Matrix4x4 frame, float a0, float a1, float y, float at, bool alongX, bool show)
    {
        if (!show)
            return;
        const float step = 0.25f, h = 0.12f, deep = 0.006f;
        var k = 0;
        for (var a = a0; a < a1 - 0.01f; a += step, k++)
        {
            var b = MathF.Min(a + step, a1);
            var lo = alongX ? new Vector3(a, y, at) : new Vector3(at, y, a);
            var hi = alongX ? new Vector3(b, y + h, at + deep) : new Vector3(at + deep, y + h, b);
            gfx.Draw(Scale(lo, hi) * frame, (k & 1) == 0 ? Yellow : Black);
        }
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour) => gfx.Draw(Scale(lo, hi) * frame, colour);
}
