using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Panel Former's bed as the renderer draws it (Sean, 2026-11-24): thin rails along the bed at the work height and a lower and an upper
/// die, solid steel blocks between the bed's corner posts, that take on the head order's panel shape and close on the blank
/// (<see cref="DrawDies"/>). Nothing stands over the bed: the upper die is the machine's top. Drawn within <see cref="DrawNear"/>, from the
/// Core's work and the shared stroke clock, so every peer draws the same. Allocates nothing.
/// </summary>
public static class PanelFormerRender
{
    public const float DrawNear = 150f;
    private const float WorkY = 1f, DeckY = 0.86f; // the rails' tops (OutTableParts.WorkY), the lower bed's deck
    private const float DieTop = 3.15f, DieBody = 0.5f, OpenMost = 1.4f; // the upper die's back open (m above the foot, under the posts' caps), its body over its shape, its widest opening
    private const float DiesFine = 40f; // the dies' cells are fine within this, coarse past it
    private const float DieInset = 0.21f, FaceThick = 0.08f; // (inset: clear of the corner posts' caps)
    private static readonly Vector3 DieSteel = new(0.52f, 0.55f, 0.58f), DieFace = new(0.82f, 0.84f, 0.86f), Rail = new(0.42f, 0.44f, 0.47f);

    public static void Draw(Renderer gfx, ConstructWorld world)
    {
        var eye = gfx.CameraPosition;
        foreach (var c in world.Bodies)
        {
            if (c.MachineAsmCount == 0 || Vector3.DistanceSquared(eye, c.WorldCom) > (DrawNear + c.Radius) * (DrawNear + c.Radius))
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.Core >= c.PartCount || asm.W == 0)
                    continue;
                var d = c.Parts[asm.Core].D;
                if (!d.IsPanelFormer || d.IsLaserCutter || d.BlockRole != BlockRole.Core)
                    continue;
                var busy = !c.Parts[asm.Core].Broken && world.StoreOf(c, asm.Core) is { Busy: true };
                DrawDies(gfx, c, asm, busy, ConstructRender.Now, Vector3.DistanceSquared(eye, c.WorldCom) < (DiesFine + c.Radius) * (DiesFine + c.Radius));
            }
        }
    }

    /// <summary>
    /// The bed's dies and rails: small rails along the bed at the work height that the blank rides in on and the panel out on; a lower die
    /// on the deck under them and an upper die over them, each one solid steel across the bed between its corner posts. While the former
    /// works, both dies take on the head order's panel shape (<see cref="ConstructPanelFormer.DieShaping"/>) over the whole bed, the shape's
    /// edges carried out to the die's ends and sides, so seen head-on along the bed each die's end shows the bend it makes: the lower die
    /// rises into it and lifts the blank, the upper die comes down (<see cref="ConstructPanelFormer.PressAt"/>) and squeezes the blank into
    /// the panel, then the upper die lifts and the lower one goes flat, leaving the panel on the rails. The dies are cells (a column and a
    /// face plate turned to the shape's slope): fine across the bed, and along it only where the shape changes along.
    /// Allocates nothing.
    /// </summary>
    private static void DrawDies(Renderer gfx, Construct c, in CMachineAsm asm, bool busy, double now, bool fine)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var cell = d.BlockCell;
        var half = cell * 0.5f;
        var o = d.BlockOrigin ?? Vector3.Zero;
        var foot = o.Y - 1.6f;
        float bx0 = o.X + asm.X0 * cell - half, bx1 = o.X + (asm.X0 + asm.W - 1) * cell + half;
        float bz0 = o.Z + asm.Z0 * cell - half, bz1 = o.Z + (asm.Z0 + asm.L - 1) * cell + half;
        float xc = (bx0 + bx1) * 0.5f, zc = (bz0 + bz1) * 0.5f;
        var alongZ = asm.OutSide is 4 or 5;
        var y0 = foot + WorkY;
        // The rails: along the bed, a metre apart across it, the blank's and the panel's way over the dies.
        float across0 = alongZ ? bx0 : bz0, across1 = alongZ ? bx1 : bz1, run0 = alongZ ? bz0 : bx0, run1 = alongZ ? bz1 : bx1;
        var lanes = Math.Max(1, (int)MathF.Floor((across1 - across0 - 2 * DieInset) / 1f));
        for (var k = 0; k <= lanes; k++)
        {
            var at = (across0 + across1) * 0.5f + (k - lanes * 0.5f);
            if (alongZ)
                Box(gfx, frame, new Vector3(at - 0.015f, y0 - 0.034f, run0 + 0.01f), new Vector3(at + 0.015f, y0, run1 - 0.01f), Rail);
            else
                Box(gfx, frame, new Vector3(run0 + 0.01f, y0 - 0.034f, at - 0.015f), new Vector3(run1 - 0.01f, y0, at + 0.015f), Rail);
        }
        // Where the dies are in the stroke, and the panel's shape (none: flat dies, as a kit's loft or a plain skin has no key shape).
        var ph = ConstructPanelFormer.Phase(now);
        ConstructPanelFormer.DieInfo? die = null;
        if (busy)
            ConstructPanelFormer.Die(c, asm.Core, out die);
        float shaping = die != null ? ConstructPanelFormer.DieShaping(ph) : 0, press = busy ? ConstructPanelFormer.PressAt(ph) : 0;
        var thick = die?.Thick ?? 0.04f;
        var high = die?.High ?? 0;
        float dx0 = bx0 + DieInset, dx1 = bx1 - DieInset, dz0 = bz0 + DieInset, dz1 = bz1 - DieInset; // (inside the corner posts)
        var lowBase = foot + DeckY + 0.02f;
        var lowFlat = y0 - ConstructPanelFormer.DieUnder + ConstructPanelFormer.DieRise * shaping;
        // (the dies' faces stay the panel's shape apart, so the opening is the same everywhere: as wide as fits under the posts' caps, 0.3 m at least)
        var shut = y0 + 0.007f + thick;
        var open = MathF.Max(shut + 0.3f, MathF.Min(shut + OpenMost, foot + DieTop - DieBody - high));
        var upFlat = shut + (open - shut) * (1 - press);
        var upTop = upFlat + high * shaping + DieBody; // (the upper die's back: flat, its body over the deepest of its shape)
        if (die == null)
        {
            Box(gfx, frame, new Vector3(dx0, lowBase, dz0), new Vector3(dx1, lowFlat, dz1), DieSteel);
            Box(gfx, frame, new Vector3(dx0, upFlat, dz0), new Vector3(dx1, upTop, dz1), DieSteel);
            return;
        }
        // A flat slab at each die's back (under the lower die's lowest face, over the upper die's highest), so the cells' sloped backs leave no gap.
        Box(gfx, frame, new Vector3(dx0, lowBase, dz0), new Vector3(dx1, MathF.Max(lowBase + 0.02f, lowFlat - FaceThick - 0.01f), dz1), DieSteel);
        Box(gfx, frame, new Vector3(dx0, MathF.Min(upTop - 0.02f, upFlat + high * shaping + FaceThick + 0.01f), dz0), new Vector3(dx1, upTop, dz1), DieSteel);
        // Cells over the whole die: fine across the bed (the bend's profile), along it only as fine as the shape changes along it.
        float acrossRes = fine ? 0.25f : 0.75f, alongRes = die.AlongFlat ? 1e9f : fine ? 0.5f : 1.5f;
        float spanX = dx1 - dx0, spanZ = dz1 - dz0;
        int nx = Math.Clamp((int)MathF.Ceiling(spanX / (alongZ ? acrossRes : alongRes)), 1, 32);
        int nz = Math.Clamp((int)MathF.Ceiling(spanZ / (alongZ ? alongRes : acrossRes)), 1, 32);
        float cx = spanX / nx, cz = spanZ / nz;
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
            {
                float ax = dx0 + i * cx, az = dz0 + j * cz;
                float h00 = Lift(die, ax, az, xc, zc, asm.OutSide), h10 = Lift(die, ax + cx, az, xc, zc, asm.OutSide);
                float h01 = Lift(die, ax, az + cz, xc, zc, asm.OutSide), h11 = Lift(die, ax + cx, az + cz, xc, zc, asm.OutSide);
                // Each die over the cell: a column with upright sides and its face sloped through the cell's corners (scaled by how far it
                // has shaped), so neighbouring cells meet edge to edge and a die's end shows the panel's profile unbroken; a bright face
                // skin on the steel.
                float mx = ax + cx * 0.5f, mz = az + cz * 0.5f, my = (h00 + h10 + h01 + h11) * 0.25f * shaping;
                float sx = (h10 + h11 - h00 - h01) * 0.5f * shaping, sz = (h01 + h11 - h00 - h10) * 0.5f * shaping;
                var slope = (MathF.Abs(sx) + MathF.Abs(sz)) * 0.5f; // (how far a sloped face's corner sits from its middle)
                var lowFace = lowFlat + my;
                Sloped(gfx, frame, mx, mz, cx, cz, sx, sz, lowFace - FaceThick, lowFace, DieFace);
                var lowFoot = lowBase + slope;
                if (lowFace - FaceThick - lowFoot > 0.01f)
                    Sloped(gfx, frame, mx, mz, cx, cz, sx, sz, lowFoot, lowFace - FaceThick, DieSteel);
                var upFace = upFlat + my;
                Sloped(gfx, frame, mx, mz, cx, cz, sx, sz, upFace, upFace + FaceThick, DieFace);
                var upBack = upTop - slope;
                if (upBack - upFace - FaceThick > 0.01f)
                    Sloped(gfx, frame, mx, mz, cx, cz, sx, sz, upFace + FaceThick, upBack, DieSteel);
            }
    }

    /// <summary>
    /// A column over the cell centred at (<paramref name="mx"/>, <paramref name="mz"/>), <paramref name="cx"/> by <paramref name="cz"/>, its
    /// sides upright and its top and bottom sloped <paramref name="sx"/> across x and <paramref name="sz"/> across z, from <paramref name="y0"/>
    /// to <paramref name="y1"/> at its middle: a unit cube sheared, so neighbours with the same corner heights meet edge to edge.
    /// </summary>
    private static void Sloped(Renderer gfx, Matrix4x4 frame, float mx, float mz, float cx, float cz, float sx, float sz, float y0, float y1, Vector3 colour)
    {
        var m = new Matrix4x4(cx, sx, 0, 0, 0, y1 - y0, 0, 0, 0, sz, cz, 0, mx, (y0 + y1) * 0.5f, mz, 1);
        gfx.Draw(m * frame, colour);
    }

    /// <summary>
    /// The panel's lift (m over its lowest point) over the Core-frame point (<paramref name="x"/>, <paramref name="z"/>), laid as
    /// ConstructPanelFormer.BedLook lays it: centred on the bed, its length along z on a bed that runs along z (either way), along x
    /// (a quarter turn, its across then against z) on one that runs along x.
    /// </summary>
    private static float Lift(ConstructPanelFormer.DieInfo die, float x, float z, float xc, float zc, byte outSide) =>
        outSide is 4 or 5 ? die.LiftAt(z - zc, x - xc) : die.LiftAt(x - xc, zc - z);

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour) => gfx.Draw(Scale(lo, hi) * frame, colour);
}
