using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a Panel Former draws across its blocks once Mast Blocks stand on all four of its bed's corners (ConstructPanelFormer.PinDepth:
/// each level of corner masts gives the pins 2 m more stroke): the deep head, a ring of girders tying the mast tops, a cross girder over
/// each Bed Block row with the oil main along one side and a drop hose down to each row's crossheads; on the inner corner of each corner
/// mast a long booster cylinder from the crosshead up to the girders, its crosshead collar riding up and down with the pins' stroke while
/// the machine works; a lamp on each corner, green while it forms. Drawn within <see cref="DrawNear"/>, from the Core's work and the
/// shared stroke clock, so every peer draws the same. Allocates nothing.
/// </summary>
public static class PanelFormerRender
{
    public const float DrawNear = 150f;
    private static readonly Vector3 Green = new(0.365f, 0.498f, 0.235f), Trim = new(0.275f, 0.376f, 0.176f), Steel = new(0.6f, 0.63f, 0.66f),
        Dark = new(0.133f, 0.149f, 0.169f), Yellow = new(0.94f, 0.76f, 0.1f), Lamp = new(0.35f, 1f, 0.4f);
    private const float BedTop = 3.2f; // a Bed Block's crosshead top (m above the Core's foot), where the first Mast Block stands

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
                var levels = Math.Min(ConstructPanelFormer.MastLevelsMost, MachineBlocks.CornerColumns(c, asm));
                if (levels == 0)
                    continue; // (no corner masts: the bed's own 2 m stroke, nothing over it)
                var working = !c.Parts[asm.Core].Broken && world.StoreOf(c, asm.Core) is { } st && ProductionLines.Working(world, c, asm.Core, st, ConstructRender.Now);
                DrawHead(gfx, c, asm, levels, working, ConstructRender.Now);
            }
        }
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
