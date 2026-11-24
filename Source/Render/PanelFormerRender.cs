using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a Panel Former draws across its blocks once Mast Blocks stand on all four of its bed's corners (ConstructPanelFormer.PinDepth:
/// a level of corner masts gives the pins 2 m more stroke): the deep head, a ring of girders tying the mast tops, a cross girder over
/// each Bed Block row with the oil main along one side and a drop hose down to each row's crossheads; on the inner corner of each corner
/// mast a long booster cylinder from the crosshead up to the girders, its crosshead collar riding up and down with the pins' stroke while
/// the machine works; a lamp on each corner, green while it forms. Drawn within <see cref="DrawNear"/>, from the Core's work and the
/// shared stroke clock, so every peer draws the same. On every Panel Former, the bed's rails and its shaped dies (<see cref="DrawDies"/>).
/// Allocates nothing.
/// </summary>
public static class PanelFormerRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Green = new(0.365f, 0.498f, 0.235f), Trim = new(0.275f, 0.376f, 0.176f), Steel = new(0.6f, 0.63f, 0.66f),
        Dark = new(0.133f, 0.149f, 0.169f), Yellow = new(0.94f, 0.76f, 0.1f), Lamp = new(0.35f, 1f, 0.4f);
    private const float BedTop = 3.2f; // a Bed Block's crosshead top (m above the Core's foot), where the first Mast Block stands
    private const float WorkY = 1f, DeckY = 0.86f, Head = 2.96f; // the rails' tops (OutTableParts.WorkY), the lower bed's deck, the crosshead's underside
    private const float DiesFine = 40f; // the shaped dies' cells are half a metre within this, coarser past it
    private const float DieInset = 0.2f, DieGap = 1f, FaceThick = 0.05f;
    private static readonly Vector3 DieSteel = new(0.52f, 0.55f, 0.58f), DieFace = new(0.66f, 0.69f, 0.72f), Rail = new(0.42f, 0.44f, 0.47f);

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
                var levels = MachineBlocks.CornerColumns(c, asm); // (its masts as they stand, a level past what deepens the stroke too: the head sits on their tops)
                if (levels == 0)
                    continue; // (no corner masts: the bed's own 2 m stroke, nothing over it)
                var working = !c.Parts[asm.Core].Broken && world.StoreOf(c, asm.Core) is { } st && ProductionLines.Working(world, c, asm.Core, st, ConstructRender.Now);
                DrawHead(gfx, c, asm, levels, working, ConstructRender.Now);
            }
        }
    }

    /// <summary>
    /// The bed's dies and rails (Sean, 2026-11-24): small rails along the bed at the work height that the blank rides in on and the panel
    /// out on; a lower die under them and an upper die hung under the crosshead, each one solid steel over the bed (inside its corner
    /// posts). While the former works, both dies take on the head order's panel shape (<see cref="ConstructPanelFormer.DieShaping"/>): the
    /// lower die rises into it and lifts the blank on its highest point, then the upper die comes down (<see cref="ConstructPanelFormer.PressAt"/>)
    /// and squeezes the blank into the panel; the upper die lifts and the lower one goes flat, leaving the panel on the rails. Over the
    /// sheet the dies are cells of half a metre (a column and a face plate turned to the shape's slope), elsewhere one flat block each.
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
        float shaping = busy ? ConstructPanelFormer.DieShaping(ph) : 0, press = busy ? ConstructPanelFormer.PressAt(ph) : 0;
        ConstructPanelFormer.DieInfo die = default;
        var shaped = busy && ConstructPanelFormer.Die(c, asm.Core, out die);
        var thick = shaped ? die.Thick : 0.04f;
        var high = shaped ? die.High - die.Low : 0;
        var gap = Math.Clamp(Head - 0.08f - (WorkY + 0.007f + thick + high), 0, DieGap); // (open: the upper die's lift, under the crosshead)
        float dx0 = bx0 + DieInset, dx1 = bx1 - DieInset, dz0 = bz0 + DieInset, dz1 = bz1 - DieInset; // (inside the corner posts)
        float lowBase = foot + DeckY + 0.02f, upTop = foot + Head - 0.004f;
        // The sheet's footprint over the bed (none when flat), and the flat blocks round it.
        float sx0 = xc, sx1 = xc, sz0 = zc, sz1 = zc;
        if (shaped)
        {
            float hl = die.Length * 0.5f, hw = die.Width * 0.5f;
            (sx0, sx1, sz0, sz1) = alongZ ? (xc - hw, xc + hw, zc - hl, zc + hl) : (xc - hl, xc + hl, zc - hw, zc + hw);
            (sx0, sx1, sz0, sz1) = (Math.Clamp(sx0, dx0, dx1), Math.Clamp(sx1, dx0, dx1), Math.Clamp(sz0, dz0, dz1), Math.Clamp(sz1, dz0, dz1));
        }
        float lowFlat = y0 - ConstructPanelFormer.DieUnder + ConstructPanelFormer.DieRise * shaping;
        float upFlat = y0 + 0.007f + thick + gap * (1 - press);
        FlatDies(gfx, frame, dx0, dx1, dz0, sz0, lowBase, lowFlat, upFlat, upTop); // (before the sheet, along z)
        FlatDies(gfx, frame, dx0, dx1, sz1, dz1, lowBase, lowFlat, upFlat, upTop); // (after it)
        FlatDies(gfx, frame, dx0, sx0, sz0, sz1, lowBase, lowFlat, upFlat, upTop); // (beside it)
        FlatDies(gfx, frame, sx1, dx1, sz0, sz1, lowBase, lowFlat, upFlat, upTop);
        if (!shaped || sx1 - sx0 < 0.01f || sz1 - sz0 < 0.01f)
            return;
        // Over the sheet: cells, each a column and a face plate turned to the shape's slope through its corners.
        var res = fine ? 0.5f : 1.5f;
        int nx = Math.Clamp((int)MathF.Ceiling((sx1 - sx0) / res), 1, 24), nz = Math.Clamp((int)MathF.Ceiling((sz1 - sz0) / res), 1, 24);
        float cx = (sx1 - sx0) / nx, cz = (sz1 - sz0) / nz;
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
            {
                float ax = sx0 + i * cx, az = sz0 + j * cz;
                float h00 = Lift(die, ax, az, xc, zc, asm.OutSide), h10 = Lift(die, ax + cx, az, xc, zc, asm.OutSide);
                float h01 = Lift(die, ax, az + cz, xc, zc, asm.OutSide), h11 = Lift(die, ax + cx, az + cz, xc, zc, asm.OutSide);
                float lo = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11)), hi = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
                var mid = new Vector3(ax + cx * 0.5f, (h00 + h10 + h01 + h11) * 0.25f, az + cz * 0.5f);
                var ta = new Vector3(cx, (h10 + h11 - h00 - h01) * 0.5f, 0);
                var tb = new Vector3(0, (h01 + h11 - h00 - h10) * 0.5f, cz);
                var n = Vector3.Normalize(Vector3.Cross(tb, ta));
                var plate = FaceThick + (hi - lo) * shaping; // (thick enough to meet its column at the cell's far corner)
                // The lower die: its face at the shape (scaled by how far it has shaped), the column under it.
                var lowAt = lowFlat + mid.Y * shaping;
                Plate(gfx, frame, new Vector3(mid.X, lowAt, mid.Z) - n * (plate * 0.5f), ta * new Vector3(1, shaping, 1), tb * new Vector3(1, shaping, 1), n, plate, DieFace);
                Box(gfx, frame, new Vector3(ax, lowBase, az), new Vector3(ax + cx, lowFlat + lo * shaping - 0.01f, az + cz), DieSteel);
                // The upper die: its face the panel's top (scaled the same), lifted by the open gap, the column over it to the crosshead.
                var upAt = upFlat + mid.Y * shaping;
                Plate(gfx, frame, new Vector3(mid.X, upAt, mid.Z) + n * (plate * 0.5f), ta * new Vector3(1, shaping, 1), tb * new Vector3(1, shaping, 1), n, plate, DieFace);
                Box(gfx, frame, new Vector3(ax, MathF.Min(upFlat + hi * shaping + 0.01f, upTop - 0.01f), az), new Vector3(ax + cx, upTop, az + cz), DieSteel);
            }
    }

    /// <summary>The flat dies over one rectangle of the bed (x and z in the Core's frame): the lower die from the deck to its face, the upper from its face to the crosshead.</summary>
    private static void FlatDies(Renderer gfx, Matrix4x4 frame, float x0, float x1, float z0, float z1, float lowBase, float lowFace, float upFace, float upTop)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f)
            return;
        Box(gfx, frame, new Vector3(x0, lowBase, z0), new Vector3(x1, lowFace, z1), DieSteel);
        Box(gfx, frame, new Vector3(x0, MathF.Min(upFace, upTop - 0.01f), z0), new Vector3(x1, upTop, z1), DieSteel);
    }

    /// <summary>A plate centred at <paramref name="at"/>, spanning <paramref name="ta"/> and <paramref name="tb"/>, <paramref name="thick"/> along <paramref name="n"/> (its top lit by n).</summary>
    private static void Plate(Renderer gfx, Matrix4x4 frame, Vector3 at, Vector3 ta, Vector3 tb, Vector3 n, float thick, Vector3 colour)
    {
        var up = n * thick;
        var m = new Matrix4x4(ta.X, ta.Y, ta.Z, 0, up.X, up.Y, up.Z, 0, tb.X, tb.Y, tb.Z, 0, at.X, at.Y, at.Z, 1);
        gfx.Draw(m * frame, colour);
    }

    /// <summary>The panel's lift (m over its lowest point) over the Core-frame point (<paramref name="x"/>, <paramref name="z"/>), the sheet laid along the bed and centred on it.</summary>
    private static float Lift(in ConstructPanelFormer.DieInfo die, float x, float z, float xc, float zc, byte outSide)
    {
        var (a, b) = outSide switch
        {
            4 => (z - zc, x - xc),
            5 => (zc - z, xc - x),
            0 => (x - xc, zc - z),
            _ => (xc - x, z - zc),
        };
        return die.Lift((a + die.Length * 0.5f) / MathF.Max(0.01f, die.Length), b / MathF.Max(0.01f, die.Width * 0.5f));
    }

    private static void DrawHead(Renderer gfx, Construct c, in CMachineAsm asm, int levels, bool working, double now)
    {
        var d = c.Parts[asm.Core].D;
        ConstructRender.PartDrawn(c, asm.Core, out var pos, out var rot);
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var cell = d.BlockCell;
        var half = cell * 0.5f;
        var o = d.BlockOrigin ?? Vector3.Zero;
        var foot = o.Y - 1.6f; // (the Core names its cell's middle 1.6 m up its 3.2 m)
        float X(int x) => o.X + x * cell;
        float Z(int z) => o.Z + z * cell;
        float x0 = X(asm.X0) - half, x1 = X(asm.X0 + asm.W - 1) + half, z0 = Z(asm.Z0) - half, z1 = Z(asm.Z0 + asm.L - 1) + half;
        var top = foot + BedTop + levels * cell; // (the masts' top)
        var stroke = working ? StrokeAt(ConstructPanelFormer.Phase(now)) : 0f;
        // ---- The ring girders on the mast tops: an I section (flanges and web) round the bed's outline, 0.15 m in from its edge ----
        const float gw = 0.3f, gh = 0.5f, inset = 0.15f;
        float ax0 = x0 + inset, ax1 = x1 - inset, az0 = z0 + inset, az1 = z1 - inset;
        Girder(gfx, frame, new Vector3(ax0, top, az0), new Vector3(ax1, top, az0 + gw), true);
        Girder(gfx, frame, new Vector3(ax0, top, az1 - gw), new Vector3(ax1, top, az1), true);
        Girder(gfx, frame, new Vector3(ax0, top + 0.004f, az0 + gw + 0.004f), new Vector3(ax0 + gw, top + 0.004f, az1 - gw - 0.004f), false);
        Girder(gfx, frame, new Vector3(ax1 - gw, top + 0.004f, az0 + gw + 0.004f), new Vector3(ax1, top + 0.004f, az1 - gw - 0.004f), false);
        // Cross girders over each block row's seam (lighter, a little under the ring) and the drop hoses to the crossheads.
        var alongX = asm.OutSide is 0 or 1; // (the bed runs from the Core along x: its rows lie along z)
        var rows = alongX ? asm.W : asm.L;
        for (var r = 1; r < rows; r++)
        {
            var at = alongX ? x0 + r * cell : z0 + r * cell;
            if (alongX)
                Box(gfx, frame, new Vector3(at - 0.09f, top + 0.06f, az0 + gw + 0.01f), new Vector3(at + 0.09f, top + gh - 0.08f, az1 - gw - 0.01f), Trim);
            else
                Box(gfx, frame, new Vector3(ax0 + gw + 0.01f, top + 0.06f, at - 0.09f), new Vector3(ax1 - gw - 0.01f, top + gh - 0.08f, at + 0.09f), Trim);
        }
        // The oil main along the +x girder's inner face and a drop hose down to each cell's crosshead under it.
        var mainX = ax1 - gw - 0.09f;
        Box(gfx, frame, new Vector3(mainX - 0.06f, top + 0.12f, az0 + gw + 0.02f), new Vector3(mainX + 0.06f, top + 0.24f, az1 - gw - 0.02f), Steel);
        for (var z = asm.Z0; z < asm.Z0 + asm.L; z++)
        {
            var hz = Z(z) + 0.35f;
            Box(gfx, frame, new Vector3(mainX - 0.035f, foot + BedTop + 0.65f, hz - 0.035f), new Vector3(mainX + 0.035f, top + 0.12f, hz + 0.035f), Dark);
            Box(gfx, frame, new Vector3(mainX - 0.07f, top + 0.1f, hz - 0.07f), new Vector3(mainX + 0.07f, top + 0.26f, hz + 0.07f), Yellow); // (its valve)
        }
        // ---- The booster cylinders on each corner mast's inner corner, their collars riding with the stroke, and the corner lamps ----
        for (var corner = 0; corner < 4; corner++)
        {
            var cx = (corner & 1) == 0 ? asm.X0 : asm.X0 + asm.W - 1;
            var cz = (corner & 2) == 0 ? asm.Z0 : asm.Z0 + asm.L - 1;
            var sx = cx == asm.X0 ? 1f : -1f; // (towards the bed's middle)
            var sz = cz == asm.Z0 ? 1f : -1f;
            if (asm.W == 1)
                sx = 1f;
            if (asm.L == 1)
                sz = 1f;
            var inner = new Vector2(X(cx) + sx * (half + 0.12f), Z(cz) + sz * (half + 0.12f));
            const float r = 0.11f;
            float ylo = foot + BedTop + 0.62f, yhi = top - 0.02f; // (from over the crosshead's ram to under the ring)
            Box(gfx, frame, new Vector3(inner.X - r, ylo, inner.Y - r), new Vector3(inner.X + r, yhi, inner.Y + r), Green);
            for (var k = 0; k <= levels; k++) // (a clamp to the mast every 2 m)
            {
                var by = MathF.Min(yhi - 0.1f, ylo + 0.2f + k * cell);
                Box(gfx, frame, new Vector3(inner.X - r - 0.04f, by - 0.06f, inner.Y - r - 0.04f), new Vector3(inner.X + r + 0.04f, by + 0.06f, inner.Y + r + 0.04f), Dark);
            }
            var collar = ylo + 0.35f + (yhi - ylo - 0.7f) * stroke;
            Box(gfx, frame, new Vector3(inner.X - r - 0.07f, collar - 0.12f, inner.Y - r - 0.07f), new Vector3(inner.X + r + 0.07f, collar + 0.12f, inner.Y + r + 0.07f), Yellow);
            var lampOn = working && MathF.Sin((float)now * MathF.Tau * 0.75f + corner) > -0.3f;
            var lx = X(cx) - sx * (half - 0.25f);
            var lz = Z(cz) - sz * (half - 0.25f);
            Box(gfx, frame, new Vector3(lx - 0.1f, top + gh, lz - 0.1f), new Vector3(lx + 0.1f, top + gh + 0.06f, lz + 0.1f), Dark);
            gfx.Draw(Scale(new Vector3(lx - 0.07f, top + gh + 0.06f, lz - 0.07f), new Vector3(lx + 0.07f, top + gh + 0.2f, lz + 0.07f)) * frame,
                lampOn ? Lamp : Lamp * 0.2f, mode: lampOn ? 2 : 0);
        }
    }

    /// <summary>The boosters' stroke at <paramref name="phase"/> of a cycle, 0 to 1: they push as the pins rise, hold while the platen presses, then come back.</summary>
    private static float StrokeAt(float phase)
    {
        const float set = ConstructPanelFormer.SetSeconds / ConstructPanelFormer.CycleSeconds;
        var up = Math.Clamp(phase / set, 0f, 1f);
        var down = Math.Clamp((phase - 0.9f) / 0.1f, 0f, 1f);
        up = up * up * (3 - 2 * up);
        down = down * down * (3 - 2 * down);
        return up * (1 - down);
    }

    /// <summary>An I-section girder from <paramref name="lo"/> to <paramref name="hi"/> (their y the foot; along x when <paramref name="alongX"/>, else along z): flanges and a web.</summary>
    private static void Girder(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, bool alongX)
    {
        const float h = 0.5f, fl = 0.05f, web = 0.05f;
        Box(gfx, frame, lo, new Vector3(hi.X, lo.Y + fl, hi.Z), Green);
        Box(gfx, frame, new Vector3(lo.X, lo.Y + h - fl, lo.Z), new Vector3(hi.X, lo.Y + h, hi.Z), Green);
        var mid = alongX ? (lo.Z + hi.Z) * 0.5f : (lo.X + hi.X) * 0.5f;
        if (alongX)
            Box(gfx, frame, new Vector3(lo.X + 0.004f, lo.Y + fl, mid - web * 0.5f), new Vector3(hi.X - 0.004f, lo.Y + h - fl, mid + web * 0.5f), Trim);
        else
            Box(gfx, frame, new Vector3(mid - web * 0.5f, lo.Y + fl, lo.Z + 0.004f), new Vector3(mid + web * 0.5f, lo.Y + h - fl, hi.Z - 0.004f), Trim);
        // A hazard band on the flange's outer edge (the outer face of a ring girder).
        if (alongX)
            Box(gfx, frame, new Vector3(lo.X + 0.004f, lo.Y + h - fl - 0.002f, lo.Z - 0.006f), new Vector3(hi.X - 0.004f, lo.Y + h + 0.002f, lo.Z - 0.002f), Yellow);
    }

    private static Matrix4x4 Scale(Vector3 lo, Vector3 hi) => Matrix4x4.CreateScale(Vector3.Max(hi - lo, new Vector3(0.001f))) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f);

    private static void Box(Renderer gfx, Matrix4x4 frame, Vector3 lo, Vector3 hi, Vector3 colour) => gfx.Draw(Scale(lo, hi) * frame, colour);
}
